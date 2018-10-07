// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : LoggerImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.Abstractions {
    using System;
    using System.Diagnostics;

    internal interface ILogger {
        void LogInformation(string message, params object[] args);
        void LogWarning(string message, params object[] args);
        void LogError(string message, params object[] args);
    }

    internal class LoggerImpl : ILogger {
        private readonly string _name;

        public LoggerImpl(string name) {
            this._name = name;
        }


        public void LogInformation(string message, params object[] args) {
            Trace.TraceInformation(this.LogFormat(message, args));
        }

        public void LogWarning(string message, params object[] args) {
            Trace.TraceWarning(this.LogFormat(message, args));
        }

        public void LogError(string message, params object[] args) {
            Trace.TraceError(this.LogFormat(message, args));
        }

        private string LogFormat(string message, params object[] args) {
            if (args.Length == 0) {
                return this._name + ": " + message;
            }

            return this._name + ": " + String.Format(message, args);
        }
    }
}