using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Series
{
    public class SeriesBookLinkResource : RestResource
    {
        public string Position { get; set; }
        public int SeriesPosition { get; set; }
        public int SeriesId { get; set; }
        public int BookId { get; set; }
        public string SeriesTitle { get; set; }
        public bool IsPrimary { get; set; }

        // User-set overrides. Null clears the override and falls back to the metadata provider's value.
        public string TitleOverride { get; set; }
        public string PositionOverride { get; set; }
        public bool? IsPrimaryOverride { get; set; }

        // What will actually be used when building file/folder names, after overrides are applied.
        public string EffectiveTitle { get; set; }
        public string EffectivePosition { get; set; }
        public bool EffectiveIsPrimary { get; set; }
    }

    public static class SeriesBookLinkResourceMapper
    {
        public static SeriesBookLinkResource ToResource(this SeriesBookLink model)
        {
            if (model == null)
            {
                return null;
            }

            return new SeriesBookLinkResource
            {
                Id = model.Id,
                Position = model.Position,
                SeriesPosition = model.SeriesPosition,
                SeriesId = model.SeriesId,
                BookId = model.BookId,
                SeriesTitle = model.Series?.Value?.Title,
                IsPrimary = model.IsPrimary,
                TitleOverride = model.TitleOverride,
                PositionOverride = model.PositionOverride,
                IsPrimaryOverride = model.IsPrimaryOverride,
                EffectiveTitle = model.TitleOverride.IsNotNullOrWhiteSpace() ? model.TitleOverride : model.Series?.Value?.Title,
                EffectivePosition = model.PositionOverride.IsNotNullOrWhiteSpace() ? model.PositionOverride : model.Position,
                EffectiveIsPrimary = model.IsPrimaryOverride ?? model.IsPrimary
            };
        }

        public static List<SeriesBookLinkResource> ToResource(this IEnumerable<SeriesBookLink> models)
        {
            return models?.Select(ToResource).ToList();
        }

        public static void ApplyOverrides(this SeriesBookLinkResource resource, SeriesBookLink model)
        {
            model.TitleOverride = resource.TitleOverride;
            model.PositionOverride = resource.PositionOverride;
            model.IsPrimaryOverride = resource.IsPrimaryOverride;
        }
    }
}
