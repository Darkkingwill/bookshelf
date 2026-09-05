using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Books;
using Readarr.Http;

namespace Readarr.Api.V1.Series
{
    [V1ApiController]
    public class SeriesController : Controller
    {
        protected readonly ISeriesService _seriesService;
        protected readonly ISeriesBookLinkService _seriesBookLinkService;

        public SeriesController(ISeriesService seriesService, ISeriesBookLinkService seriesBookLinkService)
        {
            _seriesService = seriesService;
            _seriesBookLinkService = seriesBookLinkService;
        }

        [HttpGet]
        public List<SeriesResource> GetSeries(int authorId)
        {
            return _seriesService.GetByAuthorId(authorId).ToResource();
        }

        [HttpGet("link")]
        public List<SeriesBookLinkResource> GetLinksByBook([FromQuery]List<int> bookIds)
        {
            return _seriesBookLinkService.GetLinksByBook(bookIds).ToResource();
        }

        [HttpPut("link")]
        public List<SeriesBookLinkResource> UpdateLinkOverrides([FromBody]List<SeriesBookLinkResource> resources)
        {
            var ids = resources.Select(r => r.Id).ToList();
            var links = _seriesBookLinkService.GetLinksByBook(resources.Select(r => r.BookId).Distinct().ToList())
                .Where(l => ids.Contains(l.Id))
                .ToDictionary(l => l.Id);

            var updated = new List<SeriesBookLink>();

            foreach (var resource in resources)
            {
                if (links.TryGetValue(resource.Id, out var link))
                {
                    resource.ApplyOverrides(link);
                    updated.Add(link);
                }
            }

            _seriesBookLinkService.UpdateMany(updated);

            return updated.ToResource();
        }
    }
}
