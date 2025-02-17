using System;

namespace OPCUaStandard.Exceptions
{
    /// <summary>
    /// Error reading value
    /// </summary>
    internal class ReadException : Exception
    {

        /// <summary>
        /// Main method
        /// </summary>
        /// <param name="message">
        /// Error message
        /// </param>
        public ReadException(string? message) : base(message)
        {
        }
    }
}