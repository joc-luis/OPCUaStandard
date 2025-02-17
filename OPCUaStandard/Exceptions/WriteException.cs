using System;

namespace OPCUaStandard.Exceptions
{
    internal class WriteException : Exception
    {
        public WriteException(string? message) : base(message)
        {
        }
    }
}