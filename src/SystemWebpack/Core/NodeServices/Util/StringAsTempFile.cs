// ******************************************************************************
//  © 2018 Sebastiaan Dammann | damsteen.nl
// 
//      Some code is Copyright Microsoft and licensed under the  MIT license.
//      See: https://github.com/aspnet/JavaScriptServices
// 
//  File:           : StringAsTempFile.cs
//  Project         : SystemWebpack
// ******************************************************************************
namespace SystemWebpack.Core.NodeServices.Util {
    using System;
    using System.IO;
    using System.Threading;

    /// <summary>
    ///     Makes it easier to pass script files to Node in a way that's sure to clean up after the process exits.
    /// </summary>
    public sealed class StringAsTempFile : IDisposable {
        private readonly IDisposable _applicationLifetimeRegistration;
        private bool _disposedValue;
        private readonly object _fileDeletionLock = new object();
        private bool _hasDeletedTempFile;

        /// <summary>
        ///     Create a new instance of <see cref="StringAsTempFile" />.
        /// </summary>
        /// <param name="content">The contents of the temporary file to be created.</param>
        /// <param name="applicationStoppingToken">A token that indicates when the host application is stopping.</param>
        public StringAsTempFile(string content, CancellationToken applicationStoppingToken) {
            this.FileName = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            File.WriteAllText(this.FileName, content);

            // Because .NET finalizers don't reliably run when the process is terminating, also
            // add event handlers for other shutdown scenarios.
            this._applicationLifetimeRegistration = applicationStoppingToken.Register(this.EnsureTempFileDeleted);
        }

        /// <summary>
        ///     Specifies the filename of the temporary file.
        /// </summary>
        public string FileName { get; }

        /// <summary>
        ///     Disposes the instance and deletes the associated temporary file.
        /// </summary>
        public void Dispose() {
            this.DisposeImpl(true);
            GC.SuppressFinalize(this);
        }

        private void DisposeImpl(bool disposing) {
            if (!this._disposedValue) {
                if (disposing) this._applicationLifetimeRegistration.Dispose();

                this.EnsureTempFileDeleted();

                this._disposedValue = true;
            }
        }

        private void EnsureTempFileDeleted() {
            lock (this._fileDeletionLock) {
                if (!this._hasDeletedTempFile) {
                    File.Delete(this.FileName);
                    this._hasDeletedTempFile = true;
                }
            }
        }

        /// <summary>
        ///     Implements the finalization part of the IDisposable pattern by calling Dispose(false).
        /// </summary>
        ~StringAsTempFile() {
            this.DisposeImpl(false);
        }
    }
}