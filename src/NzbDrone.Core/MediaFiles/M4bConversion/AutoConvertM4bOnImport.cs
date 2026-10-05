using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.M4bConversion
{
    // Queues the M4B conversion once a freshly downloaded audiobook has been imported as something other
    // than M4B (usually an MP3 set). It only reacts to imports that came from a download client, so a
    // rescan of an existing library never starts converting books on its own.
    public class AutoConvertM4bOnImport : IHandle<BookImportedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AutoConvertM4bOnImport(IConfigService configService,
                                      IManageCommandQueue commandQueueManager,
                                      Logger logger)
        {
            _configService = configService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(BookImportedEvent message)
        {
            if (!_configService.AutoConvertToM4b || message.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var imported = message.ImportedBooks;

            // Nothing to do when there is nothing imported, or when an M4B is already among the files.
            if (imported == null || !imported.Any() || imported.Any(f => f.Quality?.Quality == Quality.M4B))
            {
                return;
            }

            _logger.Info("Converting '{0}' to M4B after import", message.Book.Title);

            _commandQueueManager.Push(new ConvertToM4bCommand(message.Book.Id));
        }
    }
}
