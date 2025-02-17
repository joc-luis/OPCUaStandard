﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using OPCUaStandard.DTO;
using OPCUaStandard.Exceptions;


namespace OPCUaStandard
{
    public class UaClient
    {
        #region Private Fields

        private readonly ConfiguredEndpoint _endpoint;
        private Session _session;
        private readonly UserIdentity _userIdentity;
        private readonly ApplicationConfiguration _appConfig;
        private const int ReconnectPeriod = 10000;
        private readonly object _lock = new object();
        private SessionReconnectHandler? _reconnectHandler;
        private ushort _namespaceIndex;

        #endregion

        #region Private methods

        private void Reconnect(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, _reconnectHandler))
            {
                return;
            }

            lock (_lock)
            {
                if (_reconnectHandler.Session != null)
                {
                    _session = (Session)_reconnectHandler.Session;
                }

                _reconnectHandler.Dispose();
                _reconnectHandler = null;
            }
        }

        private Subscription Subscription(int milliseconds)
        {
            var subscription = new Subscription()
            {
                PublishingEnabled = true,
                PublishingInterval = milliseconds,
                Priority = 1,
                KeepAliveCount = 10,
                LifetimeCount = 20,
                MaxNotificationsPerPublish = 1000
            };

            return subscription;
        }

        #endregion

        #region Public fields

        /// <summary>
        /// Indicates if the instance is connected to the server.
        /// </summary>
        public bool IsConnected => _session is { Connected: true };

        #endregion

        #region Public methods

        /// <summary>
        /// Create a new instance
        /// </summary>
        /// <param name="appName">
        /// Name of the application
        /// </param>
        /// <param name="serverUrl">
        /// Url of server
        /// </param>
        /// <param name="namespaceIndex">
        /// Namespace index
        /// </param>
        /// <param name="security">
        /// Enable or disable the security
        /// </param>
        /// <param name="untrusted">
        /// Accept untrusted certificates
        /// </param>
        /// <param name="user">
        /// User of the OPC UA Server
        /// </param>
        /// <param name="password">
        /// Password of the user
        /// </param>
        public UaClient(string appName, string serverUrl, ushort namespaceIndex = 2, bool security = false, bool untrusted = true, string user = "",
            string password = "")
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "Certificates");
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(Path.Combine(path, "Application"));
            Directory.CreateDirectory(Path.Combine(path, "Trusted"));
            Directory.CreateDirectory(Path.Combine(path, "TrustedPeer"));
            Directory.CreateDirectory(Path.Combine(path, "Rejected"));
            string hostName = Dns.GetHostName();

            _userIdentity = user.Length > 0 ? new UserIdentity(user, password) : new UserIdentity();
            _appConfig = new ApplicationConfiguration
            {
                ApplicationName = appName,
                ApplicationUri = Utils.Format(@"urn:{0}" + appName, hostName),
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StorePath = Path.Combine(path, "Application"),
                        SubjectName = $"CN={appName}, DC={hostName}"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "Trusted")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "TrustedPeer")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = @"Directory",
                        StorePath = Path.Combine(path, "Rejected")
                    },
                    AutoAcceptUntrustedCertificates = true,
                    AddAppCertToTrustedStore = true,
                    RejectSHA1SignedCertificates = false
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 20000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 5000 },
                TraceConfiguration = new TraceConfiguration
                {
                    DeleteOnLoad = true,
                },
                DisableHiResClock = false
            };
            _appConfig.Validate(ApplicationType.Client).GetAwaiter().GetResult();

            if (_appConfig.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                _appConfig.CertificateValidator.CertificateValidation += (s, ee) => { ee.Accept = (ee.Error.StatusCode == StatusCodes.BadCertificateUntrusted && untrusted); };
            }

            var application = new ApplicationInstance
            {
                ApplicationName = appName,
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = _appConfig
            };
            Utils.SetTraceMask(0);
            application.CheckApplicationInstanceCertificate(true, 2048).GetAwaiter().GetResult();

            var endpointDescription = CoreClientUtils.SelectEndpoint(_appConfig, serverUrl, security);
            var endpointConfig = EndpointConfiguration.Create(_appConfig);
            _endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);

            _namespaceIndex = namespaceIndex;
        }


        /// <summary>
        /// Change the namespace of the connection.
        /// </summary>
        /// <param name="namespaceIndex">
        ///  Index of the namespace
        /// </param>
        public void ChangeNamespaceIndex(ushort namespaceIndex)
        {
            _namespaceIndex = namespaceIndex;
        }

        /// <summary>
        /// Open the connection with the OPC UA Server
        /// </summary>
        /// <param name="timeOut">
        /// Timeout to try to connect with the server in seconds.
        /// </param>
        /// <param name="keepAlive">
        /// Sets whether to try to connect to the server in case the connection is lost.
        /// </param>
        /// <exception cref="ServerException"></exception>
        public void Connect(uint timeOut = 5, bool keepAlive = false)
        {
            Disconnect();

            _session =
                Task.Run(
                    async () => await Session.Create(_appConfig, _endpoint, false, false, _appConfig.ApplicationName,
                        timeOut * 1000, _userIdentity, null)).GetAwaiter().GetResult();

            if (keepAlive)
            {
                _session.KeepAlive += KeepAlive;
            }

            if (!(_session is { Connected: true }))
            {
                throw new ServerException("Error creating a session on the server");
            }
        }

        /// <summary>
        /// Open the connection with the OPC UA Server
        /// </summary>
        /// <param name="timeOut">
        /// Timeout to try to connect with the server in seconds.
        /// </param>
        /// <param name="keepAlive">
        /// Sets whether to try to connect to the server in case the connection is lost.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <exception cref="ServerException"></exception>
        public async Task ConnectAsync(uint timeOut = 5, bool keepAlive = false, CancellationToken ct = default)
        {
            await DisconnectAsync(ct);

            _session = await Session.Create(_appConfig, _endpoint, false, false, _appConfig.ApplicationName,
                timeOut * 1000, _userIdentity, null, ct);

            if (keepAlive)
            {
                _session.KeepAlive += KeepAlive;
            }

            if (!(_session is { Connected: true }))
            {
                throw new ServerException("Error creating a session on the server");
            }
        }

        private void KeepAlive(ISession session, KeepAliveEventArgs e)
        {
            try
            {
                if (!ServiceResult.IsBad(e.Status)) return;
                lock (_lock)
                {
                    if (_reconnectHandler != null) return;
                    _reconnectHandler = new SessionReconnectHandler(true);
                    _reconnectHandler.BeginReconnect(_session, ReconnectPeriod, Reconnect);
                }
            }
            catch (Exception ex)
            {
                // ignored
            }
        }

        /// <summary>
        /// Close the connection with the OPC UA Server
        /// </summary>
        public void Disconnect()
        {
            if (_session is { Connected: true })
            {
                if (_session.Subscriptions != null && _session.Subscriptions.Any())
                {
                    foreach (var subscription in _session.Subscriptions)
                    {
                        subscription.Delete(true);
                    }
                }

                _session.Close();
                _session.Dispose();
                _session = null;
            }
        }

        /// <summary>
        /// Close the connection with the OPC UA Server
        /// </summary>
        public async Task DisconnectAsync(CancellationToken ct = default)
        {
            if (_session is { Connected: true })
            {
                if (_session.Subscriptions != null && _session.Subscriptions.Any())
                {
                    foreach (var subscription in _session.Subscriptions)
                    {
                        await subscription.DeleteAsync(true, ct);
                    }
                }

                await _session.CloseAsync(ct);
                _session.Dispose();
                _session = null;
            }
        }


        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="value">
        /// Value to write
        /// </param>
        /// <exception cref="WriteException"></exception>
        public void Write(string address, object value)
        {
            WriteValueCollection writeValues = new WriteValueCollection();
            var writeValue = new WriteValue
            {
                NodeId = new NodeId(address, _namespaceIndex),
                AttributeId = Attributes.Value,
                Value = new DataValue
                {
                    Value = value
                }
            };
            writeValues.Add(writeValue);
            _session.Write(null, writeValues, out StatusCodeCollection statusCodes,
                out DiagnosticInfoCollection diagnosticInfo);
            if (!StatusCode.IsGood(statusCodes[0]))
            {
                throw new WriteException("Error writing value. Code: " + statusCodes[0].Code);
            }
        }


        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="tag"> <see cref="DTO.Tag"/></param>
        /// <exception cref="WriteException"></exception>
        public void Write(Tag tag)
        {
            Write(tag.Address, tag.Value);
        }


        /// <summary>
        /// Read a tag of the specific address
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <returns>
        /// <see cref="Tag"/>
        /// </returns>
        public Tag Read(string address)
        {
            var tag = new Tag
            {
                Address = address,
                Value = null,
            };
            ReadValueIdCollection readValues = new ReadValueIdCollection()
            {
                new ReadValueId
                {
                    NodeId = new NodeId(address, _namespaceIndex),
                    AttributeId = Attributes.Value
                }
            };
            _session.Read(null, 0, TimestampsToReturn.Both, readValues, out DataValueCollection dataValues,
                out DiagnosticInfoCollection diagnosticInfo);


            tag.Value = dataValues[0].Value;
            tag.Code = dataValues[0].StatusCode;
            return tag;
        }


        /// <summary>
        /// Read an address
        /// </summary>
        /// <param name="address">
        /// Address to read.
        /// </param>
        /// <typeparam name="TValue">
        /// Type of value to read.
        /// </typeparam>
        /// <returns></returns>
        /// <exception cref="ReadException">
        /// If the status of read action is not good <see cref="StatusCodes"/>
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// If the type is not supported.
        /// </exception>
        public TValue Read<TValue>(string address)
        {
            ReadValueIdCollection readValues = new ReadValueIdCollection()
            {
                new ReadValueId
                {
                    NodeId = new NodeId(address, _namespaceIndex),
                    AttributeId = Attributes.Value
                }
            };


            _session.Read(null, 0, TimestampsToReturn.Both, readValues, out DataValueCollection dataValues,
                out var diagnosticInfo);

            if (dataValues[0].StatusCode != StatusCodes.Good)
            {
                
                throw new ReadException(dataValues[0].StatusCode.Code.ToString());
            }
            
            return (TValue)Convert.ChangeType(dataValues[0].Value, typeof(TValue));
        }


        /// <summary>
        /// Write a lis of values
        /// </summary>
        /// <param name="tags"> <see cref="Tag"/></param>
        /// <exception cref="WriteException"></exception>
        public void Write(List<Tag> tags)
        {
            WriteValueCollection writeValues = new WriteValueCollection();


            writeValues.AddRange(tags.Select(tag => new WriteValue
            {
                NodeId = new NodeId(tag.Address, _namespaceIndex),
                AttributeId = Attributes.Value,
                Value = new DataValue()
                {
                    Value = tag.Value
                }
            }));
            _session.Write(null, writeValues, out StatusCodeCollection statusCodes,
                out DiagnosticInfoCollection diagnosticInfo);

            if (statusCodes.All(StatusCode.IsGood)) return;
            {
                var status = statusCodes.First(sc => !StatusCode.IsGood(sc));
                throw new WriteException("Error writing value. Code: " + status.Code.ToString());
            }
        }


        /// <summary>
        /// Read a list of tags on the OPCUA Server
        /// </summary>
        /// <param name="address">
        /// List of address to read.
        /// </param>
        /// <returns>
        /// A list of tags <see cref="Tag"/>
        /// </returns>
        public List<Tag> Read(List<string> address)
        {
            var tags = new List<Tag>();

            ReadValueIdCollection readValues = new ReadValueIdCollection();
            readValues.AddRange(address.Select(a => new ReadValueId
            {
                NodeId = new NodeId(a, _namespaceIndex),
                AttributeId = Attributes.Value
            }));

            _session.Read(null, 0, TimestampsToReturn.Both, readValues, out DataValueCollection dataValues,
                out DiagnosticInfoCollection diagnosticInfo);

            for (int i = 0; i < address.Count; i++)
            {
                tags.Add(new Tag
                {
                    Address = address[i],
                    Value = dataValues[i].Value,
                    Code = dataValues[i].StatusCode
                });
            }

            return tags;
        }


        /// <summary>
        /// Monitoring a tag and execute a function when the value change
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        public void Monitoring(string address, int milliseconds, MonitoredItemNotificationEventHandler monitor)
        {
            var subscription = Subscription(milliseconds);
            var monitored = new MonitoredItem
            {
                StartNodeId = new NodeId(address, _namespaceIndex),
                AttributeId = Attributes.Value
            };
            monitored.Notification += monitor;
            subscription.AddItem(monitored);
            _session.AddSubscription(subscription);
            subscription.Create();
            subscription.ApplyChanges();
        }

        /// <summary>
        /// Monitoring a tag and execute a function when the value change
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="milliseconds">
        /// Sets the time to check changes in the tag
        /// </param>
        /// <param name="monitor">
        /// Function to execute when the value changes.
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task MonitoringAsync(string address, int milliseconds, MonitoredItemNotificationEventHandler monitor, CancellationToken ct)
        {
            var subscription = Subscription(milliseconds);
            var monitored = new MonitoredItem
            {
                StartNodeId = new NodeId(address, _namespaceIndex),
                AttributeId = Attributes.Value
            };
            monitored.Notification += monitor;
            subscription.AddItem(monitored);
            _session.AddSubscription(subscription);
            await subscription.CreateAsync(ct);
            await subscription.ApplyChangesAsync(ct);
        }


        /// <summary>
        /// Scan root folder of OPC UA server and get all devices
        /// </summary>
        /// <param name="recursive">
        /// Indicates whether to search within device groups
        /// </param>
        /// <returns>
        /// List of <see cref="Device"/>
        /// </returns>
        public List<Device> Devices(bool recursive = false)
        {
            Browser browser = new Browser(_session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            ReferenceDescriptionCollection browseResults = browser.Browse(ObjectIds.ObjectsFolder);

            var devices = browseResults.Where(d => d.ToString() != "Server").Select(b => new Device
            {
                Address = b.ToString()
            }).ToList();

            devices.ForEach(d =>
            {
                d.Groups = Groups(d.Address, recursive);
                d.Tags = Tags(d.Address);
            });

            return devices;
        }


        /// <summary>
        /// Scan an address and retrieve the tags and groups
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="recursive">
        /// Indicates whether to search within group groups
        /// </param>
        /// <returns>
        /// List of <see cref="System.Text.RegularExpressions.Group"/>
        /// </returns>
        public List<Group> Groups(string address, bool recursive = false)
        {
            var groups = new List<Group>();
            Browser browser = new Browser(_session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            ReferenceDescriptionCollection browseResults = browser.Browse(new NodeId(address, _namespaceIndex));

            foreach (var result in browseResults)
            {
                if (result.NodeClass != NodeClass.Object) continue;
                var group = new Group
                {
                    Address = address + "." + result.ToString()
                };
                group.Groups = Groups(group.Address, recursive);
                group.Tags = Tags(group.Address);
                groups.Add(group);
            }

            return groups;
        }


        /// <summary>
        /// Scan an address and retrieve the tags.
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="nameSpaceIndex">
        /// Index of the namespace
        /// </param>
        /// <returns>
        /// List of <see cref="Tag"/>
        /// </returns>
        public List<Tag> Tags(string address)
        {
            var tags = new List<Tag>();
            var browser = new Browser(_session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
            };

            var browseResults = browser.Browse(new NodeId(address, _namespaceIndex));
            foreach (var result in browseResults)
            {
                if (result.NodeClass == NodeClass.Variable)
                {
                    tags.Add(new Tag
                    {
                        Address = address + "." + result
                    });
                }
            }

            return tags;
        }


        #region Async methods

        /// <summary>
        /// Scan root folder of OPC UA server and get all devices
        /// </summary>
        /// <param name="recursive">
        /// Indicates whether to search within device groups
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Device"/>
        /// </returns>
        public Task<List<Device>> DevicesAsync(bool recursive = false, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                Browser browser = new Browser(_session)
                {
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
                };

                ReferenceDescriptionCollection browseResults = browser.Browse(ObjectIds.ObjectsFolder);

                var devices = browseResults.Where(d => d.ToString() != "Server").Select(b => new Device
                {
                    Address = b.ToString()
                }).ToList();

                devices.ForEach(d =>
                {
                    d.Groups = Groups(d.Address, recursive);
                    d.Tags = Tags(d.Address);
                });
                return devices;
            }, ct);
        }


        /// <summary>
        /// Scan an address and retrieve the tags and groups
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="recursive">
        /// Indicates whether to search within group groups
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Group"/>
        /// </returns>
        public Task<List<Group>> GroupsAsync(string address, bool recursive = false, CancellationToken ct = default)
        {
            return Task.Run(() => Groups(address, recursive), ct);
        }


        /// <summary>
        /// Scan an address and retrieve the tags.
        /// </summary>
        /// <param name="address">
        /// Address to search
        /// </param>
        /// <param name="ct">
        ///  Cancellation token
        /// </param>
        /// <returns>
        /// List of <see cref="Tag"/>
        /// </returns>
        public Task<List<Tag>> TagsAsync(string address, CancellationToken ct = default)
        {
            return Task.Run(() => Tags(address), ct);
        }


        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="value">
        /// Value to write
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task<Tag> WriteAsync(string address, Object value, CancellationToken ct = default)
        {
            var writeValues = new WriteValueCollection();
            var writeValue = new WriteValue
            {
                NodeId = new NodeId(address, _namespaceIndex),
                AttributeId = Attributes.Value,
                Value = new DataValue
                {
                    Value = value
                }
            };
            writeValues.Add(writeValue);
            var response = await _session.WriteAsync(null, writeValues, ct);

            var tag = new Tag()
            {
                Address = address,
                Value = value,
                Code = response.Results[0].Code
            };

            return tag;
        }


        /// <summary>
        /// Write a value on a tag
        /// </summary>
        /// <param name="tag"> <see cref="Tag"/></param>
        /// <param name="ct"> Cancellation token</param>
        public Task<Tag> WriteAsync(Tag tag, CancellationToken ct = default)
        {
            var task = WriteAsync(tag.Address, tag.Value, ct);

            return task;
        }

        /// <summary>
        /// Write a lis of values
        /// </summary>
        /// <param name="tags"><see cref="Tag"/></param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        public async Task<IEnumerable<Tag>> WriteAsync(List<Tag> tags, CancellationToken ct = default)
        {
            var writeValues = new WriteValueCollection();


            writeValues.AddRange(tags.Select(tag => new WriteValue
            {
                NodeId = new NodeId(tag.Address, _namespaceIndex),
                AttributeId = Attributes.Value,
                Value = new DataValue()
                {
                    Value = tag.Value
                }
            }));

            var response = await _session.WriteAsync(null, writeValues, ct);

            for (int i = 0; i < response.Results.Count; i++)
            {
                tags[i].Code = response.Results[i].Code;
            }

            return tags;
        }


        /// <summary>
        /// Read a tag of the specific address
        /// </summary>
        /// <param name="address">
        /// Address of the tag
        /// </param>
        /// <param name="ct">
        /// Cancellation token
        /// </param>
        /// <returns>
        /// <see cref="Tag"/>
        /// </returns>
        public async Task<Tag> ReadAsync(string address, CancellationToken ct = default)
        {
            var tag = new Tag
            {
                Address = address,
                Value = null,
            };
            var readValues = new ReadValueIdCollection()
            {
                new ReadValueId
                {
                    NodeId = new NodeId(address, _namespaceIndex),
                    AttributeId = Attributes.Value
                }
            };

            var dataValues = await _session.ReadAsync(null, 0, TimestampsToReturn.Both, readValues, ct);

            tag.Value = dataValues.Results[0].Value;
            tag.Code = dataValues.Results[0].StatusCode;

            return tag;
        }

        /// <summary>
        /// Read an address
        /// </summary>
        /// <param name="address">
        /// Address to read.
        /// </param>
        /// <param name="ct">
        ///  Cancellation token
        /// </param>
        /// <typeparam name="TValue">
        /// Type of value to read.
        /// </typeparam>
        /// <returns></returns>
        /// <exception cref="ReadException">
        /// If the status of read action is not good <see cref="StatusCodes"/>
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// If the type is not supported.
        /// </exception>
        public Task<TValue> ReadAsync<TValue>(string address, CancellationToken ct = default)
        {
            return Task.Run(() => Read<TValue>(address), ct);
        }


        /// <summary>
        /// Read a list of tags on the OPCUA Server
        /// </summary>
        /// <param name="address">
        /// List of address to read.
        /// </param>
        /// <param name="ct">
        ///  Cancellation token
        /// </param>
        /// <returns>
        /// A list of tags <see cref="Tag"/>
        /// </returns>
        public async Task<IEnumerable<Tag>> ReadAsync(IEnumerable<string> address, CancellationToken ct = default)
        {
            var readValues = new ReadValueIdCollection();
            readValues.AddRange(address.Select(a => new ReadValueId
            {
                NodeId = new NodeId(a, _namespaceIndex),
                AttributeId = Attributes.Value
            }));

            var dataValues =
                await _session.ReadAsync(null, 0, TimestampsToReturn.Both, readValues, ct);

            return dataValues.Results.Select((t, i) => new Tag { Address = address.ToArray()[i], Value = t.Value, Code = t.StatusCode }).ToList();
        }

        #endregion

        #endregion
    }
}