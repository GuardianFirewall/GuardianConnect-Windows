using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GuardianConnect.Shared
{
    public static class StaticLoggerFactory
    {
        private static ILoggerFactory _loggerFactory;

        public static void Initialize(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public static ILogger<T> CreateLogger<T>()
        {
            if (_loggerFactory == null)
            {
                // Handle case where factory is not initialized, e.g., throw an exception
                // or create a default NullLoggerFactory
                throw new InvalidOperationException("StaticLoggerFactory has not been initialized.");
            }
            return _loggerFactory.CreateLogger<T>();
        }

        public static ILogger CreateLogger(string categoryName)
        {
            if (_loggerFactory == null)
            {
                throw new InvalidOperationException("StaticLoggerFactory has not been initialized.");
            }
            return _loggerFactory.CreateLogger(categoryName);
        }
    }
}
