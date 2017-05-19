using System;

namespace MbientLab.MetaWear.Template
{
    internal class UnimplementedException : Exception
    {
        public UnimplementedException()
        {
        }

        public UnimplementedException(string message) : base(message)
        {
        }

        public UnimplementedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}