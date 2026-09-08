using System.Collections.Generic;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Books
{
    public interface ISeriesBookLinkService
    {
        SeriesBookLink Get(int id);
        List<SeriesBookLink> GetLinksBySeries(int seriesId);
        List<SeriesBookLink> GetLinksBySeriesAndAuthor(int seriesId, string foreignAuthorId);
        List<SeriesBookLink> GetLinksByBook(List<int> bookIds);
        SeriesBookLink Insert(SeriesBookLink model);
        void InsertMany(List<SeriesBookLink> model);
        void UpdateMany(List<SeriesBookLink> model);
        void Delete(int id);
        void DeleteMany(List<SeriesBookLink> model);
    }

    public class SeriesBookLinkService : ISeriesBookLinkService,
        IHandle<BookDeletedEvent>
    {
        private readonly ISeriesBookLinkRepository _repo;

        public SeriesBookLinkService(ISeriesBookLinkRepository repo)
        {
            _repo = repo;
        }

        public SeriesBookLink Get(int id)
        {
            return _repo.Get(id);
        }

        public List<SeriesBookLink> GetLinksBySeries(int seriesId)
        {
            return _repo.GetLinksBySeries(seriesId);
        }

        public List<SeriesBookLink> GetLinksBySeriesAndAuthor(int seriesId, string foreignAuthorId)
        {
            return _repo.GetLinksBySeriesAndAuthor(seriesId, foreignAuthorId);
        }

        public List<SeriesBookLink> GetLinksByBook(List<int> bookIds)
        {
            return _repo.GetLinksByBook(bookIds);
        }

        public SeriesBookLink Insert(SeriesBookLink model)
        {
            return _repo.Insert(model);
        }

        public void InsertMany(List<SeriesBookLink> model)
        {
            _repo.InsertMany(model);
        }

        public void UpdateMany(List<SeriesBookLink> model)
        {
            _repo.UpdateMany(model);
        }

        public void Delete(int id)
        {
            _repo.Delete(id);
        }

        public void DeleteMany(List<SeriesBookLink> model)
        {
            _repo.DeleteMany(model);
        }

        public void Handle(BookDeletedEvent message)
        {
            var links = GetLinksByBook(new List<int> { message.Book.Id });
            DeleteMany(links);
        }
    }
}
