using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Books
{
    public interface IBookMergeService
    {
        void MergeBooks(int targetBookId, List<int> sourceBookIds);
    }

    // Merges one or more duplicate Book rows into a single target book - for when the metadata
    // matching pipeline (or a manual add) left duplicate rows for what is really the same book.
    // Reuses the same steps RefreshBookService.MergeEntity already uses internally when a refresh
    // detects a duplicate, so a manually-triggered merge behaves identically to an automatic one.
    public class BookMergeService : IBookMergeService
    {
        private readonly IBookService _bookService;
        private readonly IEditionService _editionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IHistoryService _historyService;
        private readonly ISeriesBookLinkService _seriesBookLinkService;
        private readonly Logger _logger;

        public BookMergeService(IBookService bookService,
                                 IEditionService editionService,
                                 IMediaFileService mediaFileService,
                                 IHistoryService historyService,
                                 ISeriesBookLinkService seriesBookLinkService,
                                 Logger logger)
        {
            _bookService = bookService;
            _editionService = editionService;
            _mediaFileService = mediaFileService;
            _historyService = historyService;
            _seriesBookLinkService = seriesBookLinkService;
            _logger = logger;
        }

        public void MergeBooks(int targetBookId, List<int> sourceBookIds)
        {
            if (sourceBookIds.Contains(targetBookId))
            {
                throw new BadRequestException("Target book cannot also be one of the source books");
            }

            var target = _bookService.GetBook(targetBookId);
            var sourceBooks = _bookService.GetBooks(sourceBookIds);

            if (sourceBooks.Any(x => x.AuthorMetadataId != target.AuthorMetadataId))
            {
                throw new BadRequestException("All books being merged must belong to the same author");
            }

            var targetEditions = _editionService.GetEditionsByBook(targetBookId);
            var targetEdition = targetEditions.SingleOrDefault(e => e.Monitored) ?? targetEditions.First();

            var targetSeriesIds = _seriesBookLinkService.GetLinksByBook(new List<int> { targetBookId })
                .Select(x => x.SeriesId)
                .ToHashSet();

            foreach (var source in sourceBooks)
            {
                _logger.Warn($"Merging book {source} into {target}");

                var files = _mediaFileService.GetFilesByBook(source.Id);
                files.ForEach(x => x.EditionId = targetEdition.Id);
                _mediaFileService.Update(files);

                var history = _historyService.GetByBook(source.Id, null);
                history.ForEach(x => x.BookId = target.Id);
                _historyService.UpdateMany(history);

                // Series links can't just be repointed at the target - if it's already linked to
                // the same series (the common case for a duplicate), moving the source's link too
                // would leave the target double-linked to that series. Only move links to series
                // the target isn't already in; drop the rest.
                var sourceLinks = _seriesBookLinkService.GetLinksByBook(new List<int> { source.Id });
                var linksToMove = sourceLinks.Where(x => !targetSeriesIds.Contains(x.SeriesId)).ToList();
                var linksToDrop = sourceLinks.Except(linksToMove).ToList();

                linksToMove.ForEach(x => x.BookId = target.Id);
                _seriesBookLinkService.UpdateMany(linksToMove);
                _seriesBookLinkService.DeleteMany(linksToDrop);

                // Keep the running set current so a later source book linked to the same series
                // gets dropped too, instead of creating a second duplicate link on the target.
                targetSeriesIds.UnionWith(linksToMove.Select(x => x.SeriesId));
            }

            _bookService.DeleteMany(sourceBooks);
        }
    }
}
