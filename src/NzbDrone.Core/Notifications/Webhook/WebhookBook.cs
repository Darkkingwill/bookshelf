using System;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.Notifications.Webhook
{
    public class WebhookBook
    {
        public WebhookBook()
        {
        }

        public WebhookBook(Book book)
        {
            Id = book.Id;
            GoodreadsId = book.ForeignBookId;
            Title = book.Title;
            ReleaseDate = book.ReleaseDate;

            var edition = book.Editions.Value.GetMonitoredEdition();
            Edition = edition == null ? null : new WebhookBookEdition(edition);
        }

        public int Id { get; set; }
        public string GoodreadsId { get; set; }
        public string Title { get; set; }
        public WebhookBookEdition Edition { get; set; }
        public DateTime? ReleaseDate { get; set; }
    }
}
