// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//  File:           : LoggerFactoryImpl.cs
//  Project         : SystemWebpack
// ******************************************************************************

namespace SystemWebpack.Core.Abstractions {
    internal interface ILoggerFactory {
        ILogger CreateLogger(string name);
    }

    internal class LoggerFactoryImpl : ILoggerFactory {
        public ILogger CreateLogger(string name) {
            return new LoggerImpl(name);
        }
    }
}