using System.Collections.Generic;

namespace OPCUaStandard.DTO
{
    /// <summary>
    /// Device on the OPC UA Server
    /// </summary>
    public class Device
    {
        /// <summary>
        /// Name of the device
        /// </summary>
        public string Name => this.Address.Substring(this.Address.LastIndexOf(".") + 1);

        /// <summary>
        /// Address of the device
        /// </summary>
        public string Address { get; set; }

        /// <summary>
        /// Groups into the device <see cref="Group"/>
        /// </summary>
        public List<Group> Groups { get; set; } = new List<Group>();

        /// <summary>
        /// Tags into the device <see cref="Tag"/>
        /// </summary>
        public List<Tag> Tags { get; set; } = new List<Tag>();
    }
}