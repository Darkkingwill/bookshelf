using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.Providers;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    [V1ApiController("config/metadatasource")]
    public class MetadataSourceController : Controller
    {
        private readonly IConfigService _configService;
        private readonly IEnumerable<IMetadataProvider> _providers;
        private readonly IMetadataProviderService _providerService;

        public MetadataSourceController(
            IConfigService configService,
            IEnumerable<IMetadataProvider> providers,
            IMetadataProviderService providerService)
        {
            _configService = configService;
            _providers = providers;
            _providerService = providerService;
        }

        [HttpGet]
        public List<MetadataSourceResource> GetAll()
        {
            // Use the service method which auto-discovers new providers
            var configs = _providerService.GetProviderConfigs();
            var available = _providers.ToList();

            return configs.Select(c =>
            {
                var provider = available.FirstOrDefault(p =>
                    p.Key.Equals(c.Key, StringComparison.OrdinalIgnoreCase));

                return new MetadataSourceResource
                {
                    Key = c.Key,
                    DisplayName = c.IsCustom ? c.DisplayName : (provider?.DisplayName ?? c.Key),
                    Enabled = c.Enabled,
                    Priority = c.Priority,
                    RequiresAuth = c.IsCustom ? !string.IsNullOrWhiteSpace(c.AuthToken) : (provider?.RequiresAuth ?? false),
                    Settings = c.Settings ?? new Dictionary<string, string>(),
                    IsCustom = c.IsCustom,
                    Url = c.Url,
                    AuthToken = c.AuthToken
                };
            }).OrderBy(r => r.Priority).ToList();
        }

        [HttpPut]
        public IActionResult SaveAll([FromBody] List<MetadataSourceResource> resources)
        {
            var configs = resources.Select(r => new MetadataProviderConfig
            {
                Key = r.Key,
                Enabled = r.Enabled,
                Priority = r.Priority,
                Settings = r.Settings,
                IsCustom = r.IsCustom,
                DisplayName = r.IsCustom ? r.DisplayName : null,
                Url = r.IsCustom ? r.Url : null,
                AuthToken = r.IsCustom ? r.AuthToken : null
            }).ToList();

            _configService.SaveMetadataProviderConfigs(configs);
            return Ok(GetAll());
        }

        [HttpPost("test")]
        public IActionResult TestProvider([FromBody] MetadataSourceTestRequest request)
        {
            var result = _providerService.TestProvider(request.Key);
            return Ok(new { success = result.Success, key = request.Key, message = result.Message });
        }
    }

    public class MetadataSourceResource
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public bool Enabled { get; set; }
        public int Priority { get; set; }
        public bool RequiresAuth { get; set; }
        public Dictionary<string, string> Settings { get; set; }
        public bool IsCustom { get; set; }
        public string Url { get; set; }
        public string AuthToken { get; set; }
    }

    public class MetadataSourceTestRequest
    {
        public string Key { get; set; }
    }
}
