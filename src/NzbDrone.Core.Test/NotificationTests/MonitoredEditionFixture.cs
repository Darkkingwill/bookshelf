using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Notifications.Webhook;

namespace NzbDrone.Core.Test.NotificationTests
{
    // A book can have no monitored edition (or several) after a refresh or a manual add; building a notification payload
    // for it must not throw.
    [TestFixture]
    public class MonitoredEditionFixture
    {
        private static Book BookWith(params Edition[] editions)
        {
            return new Book { Id = 3, Title = "Dark Tower", ForeignBookId = "42", Editions = new List<Edition>(editions) };
        }

        [Test]
        public void webhook_should_use_the_monitored_edition()
        {
            var book = BookWith(
                new Edition { ForeignEditionId = "1", Monitored = false },
                new Edition { ForeignEditionId = "2", Monitored = true });

            new WebhookBook(book).Edition.GoodreadsId.Should().Be("2");
        }

        [Test]
        public void webhook_should_fall_back_to_the_first_edition_when_none_is_monitored()
        {
            var book = BookWith(new Edition { ForeignEditionId = "1" }, new Edition { ForeignEditionId = "2" });

            new WebhookBook(book).Edition.GoodreadsId.Should().Be("1");
        }

        [Test]
        public void webhook_should_use_the_first_monitored_edition_when_several_are_monitored()
        {
            var book = BookWith(
                new Edition { ForeignEditionId = "1", Monitored = true },
                new Edition { ForeignEditionId = "2", Monitored = true });

            new WebhookBook(book).Edition.GoodreadsId.Should().Be("1");
        }

        [Test]
        public void webhook_should_have_no_edition_when_the_book_has_none()
        {
            var webhook = new WebhookBook(BookWith());

            webhook.Title.Should().Be("Dark Tower");
            webhook.Edition.Should().BeNull();
        }
    }
}
