using OPCUaStandard;

namespace Tests;

public class Tests
{
    private const string Url = "opc.tcp://localhost:52241";

    [Test, Order(1)]
    public void Connect()
    {
        var uaClient = new UaClient("Connect test", Url);
        uaClient.Connect();
        Assert.That(uaClient.IsConnected, Is.True);
        uaClient.Disconnect();
        Assert.That(uaClient.IsConnected, Is.False);
    }


    [Test, Order(2)]
    public void ReadWriteInteger()
    {
        const string address = "Device1.Integer";
        var value = new Random().Next(0, int.MaxValue);
        var uaClient = new UaClient("Read and write integer test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<int>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
    
    [Test, Order(3)]
    public void ReadWriteLong()
    {
        const string address = "Device1.Long";
        var value = new Random().NextInt64(0, long.MaxValue);
        var uaClient = new UaClient("Read and write long test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<long>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
    
    [Test, Order(4)]
    public void ReadWriteDouble()
    {
        const string address = "Device1.Double";
        var value = new Random().NextDouble();
        var uaClient = new UaClient("Read and write double test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<double>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
    
    [Test, Order(5)]
    public void ReadWriteFloat()
    {
        const string address = "Device1.Float";
        var value = new Random().NextSingle();
        var uaClient = new UaClient("Read and write float test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<float>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
    
    [Test, Order(5)]
    public void ReadWriteBoolean()
    {
        const string address = "Device1.Boolean";
        var value = new Random().Next(0, 1).Equals(0);
        var uaClient = new UaClient("Read and write boolean test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<bool>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
    
    [Test, Order(5)]
    public void ReadWriteString()
    {
        const string address = "Device1.String";
        var value = Guid.NewGuid().ToString();
        var uaClient = new UaClient("Read and write string test", Url);
        uaClient.Connect();
        uaClient.Write(address, value);
        var tag = uaClient.Read<string>(address);
        Assert.That(tag, Is.EqualTo(value));
        uaClient.Disconnect();
    }
}