using System;

namespace OPCUaStandard.Exceptions
{
    internal class ServerException : Exception
    {
        public ServerException(string? message) : base(message)
        {
        }
    }
}