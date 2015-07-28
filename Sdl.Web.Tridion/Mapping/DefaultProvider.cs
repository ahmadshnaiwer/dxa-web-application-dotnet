﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using DD4T.ContentModel;
using DD4T.ContentModel.Factories;
using Sdl.Web.Common;
using Sdl.Web.Common.Configuration;
using Sdl.Web.Common.Interfaces;
using Sdl.Web.Common.Logging;
using Sdl.Web.Common.Models;
using Sdl.Web.Tridion.Statics;
using Sdl.Web.Mvc.Configuration;
using Sdl.Web.Tridion.Query;
using Tridion.ContentDelivery.DynamicContent.Query;
using Tridion.ContentDelivery.Meta;
using IPage = DD4T.ContentModel.IPage;

namespace Sdl.Web.Tridion.Mapping
{
    /// <summary>
    /// Default Content Provider and Navigation Provider implementation (DD4T-based).
    /// </summary>
    public class DefaultProvider : IContentProvider, INavigationProvider
    {
        private readonly IPageFactory _pageFactory;
        private readonly IComponentFactory _componentFactory;

        protected IPageFactory PageFactory
        {
            get
            {
                _pageFactory.PageProvider.PublicationId = 0; // Force the DD4T PageProvider to use our PublicationResolver to determine the Publication ID.
                return _pageFactory;
            }
        }

        public DefaultProvider(IPageFactory pageFactory, IComponentFactory componentFactory)
        {
            if (pageFactory == null)
            {
                throw new DxaException("No Page Factory configured.");
            }

            _pageFactory = pageFactory;

            if (componentFactory == null)
            {
                throw new DxaException("No Component Factory configured.");
            }

            _componentFactory = componentFactory;
        }

        #region IContentProvider members
#pragma warning disable 618
        [Obsolete("Deprecated in DXA 1.1. Use SiteConfiguration.LinkResolver or SiteConfiguration.RichTextProcessor to get the new extension points.")]
        public IContentResolver ContentResolver
        {
            get
            {
                return new LegacyContentResolverFacade();
            }
            set
            {
                throw new NotSupportedException("Setting this property is not supported in DXA 1.1.");
            }
        }

        /// <summary>
        /// Gets a Page Model for a given URL.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="addIncludes">Indicates whether include Pages should be expanded.</param>
        /// <returns>The Page Model.</returns>
        [Obsolete("Deprecated in DXA 1.1. Use the overload that has a Localization parameter.")]
        public PageModel GetPageModel(string url, bool addIncludes = true)
        {
            return GetPageModel(url, WebRequestContext.Localization, addIncludes);
        }

        /// <summary>
        /// Populates a Content List by executing the query it specifies.
        /// </summary>
        /// <param name="contentList">The Content List (of Teasers) which specifies the query and is to be populated.</param>
        [Obsolete("Deprecated in DXA 1.1. Use the overload that has a Localization parameter.")]
        public ContentList<Teaser> PopulateDynamicList(ContentList<Teaser> contentList)
        {
            PopulateDynamicList(contentList, WebRequestContext.Localization);
            return contentList;
        }

#pragma warning restore 618


        /// <summary>
        /// Gets a Page Model for a given URL.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="localization">The context Localization.</param>
        /// <param name="addIncludes">Indicates whether include Pages should be expanded.</param>
        /// <returns>The Page Model.</returns>
        /// <exception cref="DxaItemNotFoundException">If no Page Model exists for the given URL.</exception>
        public virtual PageModel GetPageModel(string url, Localization localization, bool addIncludes)
        {
            using (new Tracer(url, localization, addIncludes))
            {
                //We can have a couple of tries to get the page model if there is no file extension on the url request, but it does not end in a slash:
                //1. Try adding the default extension, so /news becomes /news.html
                IPage page = GetPage(url);
                if (page == null && (url == null || (!url.EndsWith("/") && url.LastIndexOf(".", StringComparison.Ordinal) <= url.LastIndexOf("/", StringComparison.Ordinal))))
                {
                    //2. Try adding the default page, so /news becomes /news/index.html
                    page = GetPage(url + "/");
                }
                if (page == null)
                {
                    throw new DxaItemNotFoundException(url);
                }

                IPage[] includes = addIncludes ? GetIncludesFromModel(page, localization).ToArray() : new IPage[0];

                return ModelBuilderPipeline.CreatePageModel(page, includes, localization);
            }
        }

        /// <summary>
        /// Gets an Entity Model for a given Entity Identifier.
        /// </summary>
        /// <param name="id">The Entity Identifier.</param>
        /// <param name="localization">The context Localization.</param>
        /// <returns>The Entity Model.</returns>
        /// <exception cref="DxaItemNotFoundException">If no Entity Model exists for the given URL.</exception>
        public virtual EntityModel GetEntityModel(string id, Localization localization)
        {
            using (new Tracer(id, localization))
            {
                // Entity Identifier of a DCP is ComponentId-TemplateId
                if (id.Contains('-'))
                {
                    string[] identifiers = id.Split('-');
                    string componentUri = string.Format("tcm:{0}-{1}", localization.LocalizationId, identifiers[0]);
                    string templateUri = string.Format("tcm:{0}-{1}-32", localization.LocalizationId, identifiers[1]);

                    IComponent component;
                    if (_componentFactory.TryGetComponent(componentUri, out component, templateUri))
                    {
                        //var componentTcmUri = new TcmUri(componentUri);
                        var templateTcmUri = new TcmUri(templateUri);

                        var publicationCriteria = new PublicationCriteria(templateTcmUri.PublicationId);
                        var itemReferenceCriteria = new ItemReferenceCriteria(templateTcmUri.ItemId);
                        var itemTypeTypeCriteria = new ItemTypeCriteria(32);

                        var query = new global::Tridion.ContentDelivery.DynamicContent.Query.Query(
                            CriteriaFactory.And(new Criteria[] { publicationCriteria, itemReferenceCriteria, itemTypeTypeCriteria }));

                        var results = query.ExecuteEntityQuery();
                        if (results != null)
                        {
                            var componentPresentation = new ComponentPresentation
                            {
                                Component = component as Component,
                                IsDynamic = true
                            };

                            var templateMeta = (ITemplateMeta)results.FirstOrDefault();
                            var template = new ComponentTemplate
                            {
                                Id = templateUri,
                                Title = templateMeta.Title,
                                OutputFormat = templateMeta.OutputFormat
                            };

                            componentPresentation.ComponentTemplate = template;
                            return ModelBuilderPipeline.CreateEntityModel(componentPresentation, localization);
                        }
                    }
                    
                    throw new DxaItemNotFoundException(id);
                }
                
                throw new NotImplementedException("This feature will be implemented in a future release"); // TODO TSI-803
            }
        }



        /// <summary>
        /// Gets a Static Content Item for a given URL path.
        /// </summary>
        /// <param name="urlPath">The URL path.</param>
        /// <param name="localization">The context Localization.</param>
        /// <returns>The Static Content Item.</returns>
        public StaticContentItem GetStaticContentItem(string urlPath, Localization localization)
        {
            using (new Tracer(urlPath, localization))
            {
                string localFilePath = BinaryFileManager.Instance.GetCachedFile(urlPath, localization);
 
                return new StaticContentItem(
                    new FileStream(localFilePath, FileMode.Open),
                    MimeMapping.GetMimeMapping(localFilePath),
                    File.GetLastWriteTime(localFilePath), 
                    Encoding.UTF8
                    );
            }
        }

        /// <summary>
        /// Populates a Content List (of Teasers) by executing the query it specifies.
        /// </summary>
        /// <param name="contentList">The Content List which specifies the query and is to be populated.</param>
        /// <param name="localization">The context Localization.</param>
        public virtual void PopulateDynamicList<T>(ContentList<T> contentList, Localization localization) where T : EntityModel
        {
            using (new Tracer(contentList, localization))
            {
                BrokerQuery query = new BrokerQuery
                {
                    Start = contentList.Start,
                    PublicationId = Int32.Parse(localization.LocalizationId),
                    PageSize = contentList.PageSize,
                    SchemaId = MapSchema(contentList.ContentType.Key, localization),
                    Sort = contentList.Sort.Key
                };

                // TODO: For now BrokerQuery always returns Teasers
                IEnumerable<Teaser> queryResults = query.ExecuteQuery();

                ILinkResolver linkResolver = SiteConfiguration.LinkResolver;
                foreach (Teaser item in queryResults)
                {
                    item.Link.Url = linkResolver.ResolveLink(item.Link.Url);
                }

                contentList.ItemListElements = queryResults.Cast<T>().ToList();
                contentList.HasMore = query.HasMore;
            }
        }

        #endregion

        #region INavigationProvider Members

        /// <summary>
        /// Gets the Navigation Model (Sitemap) for a given Localization.
        /// </summary>
        /// <param name="localization">The Localization.</param>
        /// <returns>The Navigation Model (Sitemap root Item).</returns>
        public virtual SitemapItem GetNavigationModel(Localization localization)
        {
            using (new Tracer(localization))
            {
                string url = SiteConfiguration.LocalizeUrl("navigation.json", localization);
                // TODO TSI-110: This is a temporary measure to cache the Navigation Model per request to not retrieve and serialize 3 times per request. Comprehensive caching strategy pending
                string cacheKey = "navigation-" + url;
                SitemapItem result;
                if (HttpContext.Current.Items[cacheKey] == null)
                {
                    Log.Debug("Deserializing Navigation Model from raw content URL '{0}'", url);
                    string navigationJsonString = GetPageContent(url);
                    result = new JavaScriptSerializer().Deserialize<SitemapItem>(navigationJsonString);
                    HttpContext.Current.Items[cacheKey] = result;
                }
                else
                {
                    Log.Debug("Obtained Navigation Model from cache.");
                    result = (SitemapItem)HttpContext.Current.Items[cacheKey];
                }
                return result;
            }
        }

        /// <summary>
        /// Gets Navigation Links for the top navigation menu for the given request URL path.
        /// </summary>
        /// <param name="requestUrlPath">The request URL path.</param>
        /// <param name="localization">The Localization.</param>
        /// <returns>The Navigation Links.</returns>
        public virtual NavigationLinks GetTopNavigationLinks(string requestUrlPath, Localization localization)
        {
            using (new Tracer(requestUrlPath, localization))
            {
                NavigationLinks navigationLinks = new NavigationLinks();
                SitemapItem sitemapRoot = GetNavigationModel(localization);
                foreach (SitemapItem item in sitemapRoot.Items.Where(i => i.Visible))
                {
                    navigationLinks.Items.Add(CreateLink((item.Title == "Index") ? sitemapRoot : item));
                }
                return navigationLinks;
            }
        }

        /// <summary>
        /// Gets Navigation Links for the context navigation panel for the given request URL path.
        /// </summary>
        /// <param name="requestUrlPath">The request URL path.</param>
        /// <param name="localization">The Localization.</param>
        /// <returns>The Navigation Links.</returns>
        public virtual NavigationLinks GetContextNavigationLinks(string requestUrlPath, Localization localization)
        {
            using (new Tracer(requestUrlPath, localization))
            {
                NavigationLinks navigationLinks = new NavigationLinks();
                SitemapItem sitemapItem = GetNavigationModel(localization); // Start with Sitemap root Item.
                int levels = requestUrlPath.Split('/').Length;
                while (levels > 1 && sitemapItem.Items != null)
                {
                    SitemapItem newParent = sitemapItem.Items.FirstOrDefault(i => i.Type == "StructureGroup" && requestUrlPath.StartsWith(i.Url.ToLower()));
                    if (newParent == null)
                    {
                        break;
                    }
                    sitemapItem = newParent;
                }

                if (sitemapItem != null && sitemapItem.Items != null)
                {
                    foreach (SitemapItem item in sitemapItem.Items.Where(i => i.Visible))
                    {
                        navigationLinks.Items.Add(CreateLink(item));
                    }
                }

                return navigationLinks;
            }
        }

        /// <summary>
        /// Gets Navigation Links for the breadcrumb trail for the given request URL path.
        /// </summary>
        /// <param name="requestUrlPath">The request URL path.</param>
        /// <param name="localization">The Localization.</param>
        /// <returns>The Navigation Links.</returns>
        public virtual NavigationLinks GetBreadcrumbNavigationLinks(string requestUrlPath, Localization localization)
        {
            using (new Tracer(requestUrlPath, localization))
            {

                NavigationLinks navigationLinks = new NavigationLinks();
                int levels = requestUrlPath.Split('/').Length;
                SitemapItem sitemapItem = GetNavigationModel(localization); // Start with Sitemap root Item.
                navigationLinks.Items.Add(CreateLink(sitemapItem));
                while (levels > 1 && sitemapItem.Items != null)
                {
                    sitemapItem = sitemapItem.Items.FirstOrDefault(i => requestUrlPath.StartsWith(i.Url.ToLower()));
                    if (sitemapItem != null)
                    {
                        navigationLinks.Items.Add(CreateLink(sitemapItem));
                        levels--;
                    }
                    else
                    {
                        break;
                    }
                }
                return navigationLinks;
            }
        }

        #endregion

        /// <summary>
        /// Creates a Link Entity Model out of a SitemapItem Entity Model.
        /// </summary>
        /// <param name="sitemapItem">The SitemapItem Entity Model.</param>
        /// <returns>The Link Entity Model.</returns>
        protected static Link CreateLink(SitemapItem sitemapItem)
        {
            return new Link
            {
                Url = SiteConfiguration.LinkResolver.ResolveLink(sitemapItem.Url),
                LinkText = sitemapItem.Title
            };
        }

        /// <summary>
        /// Converts a request URL into a CMS URL (for example adding default page name, and file extension)
        /// </summary>
        /// <param name="url">The request URL</param>
        /// <returns>A CMS URL</returns>
        protected virtual string GetCmUrl(string url)
        {
            if (String.IsNullOrEmpty(url))
            {
                url = Constants.DefaultPageName;
            }
            if (url.EndsWith("/"))
            {
                url = url + Constants.DefaultPageName;
            }
            if (!Path.HasExtension(url))
            {
                url = url + Constants.DefaultExtension;
            }
            if (!url.StartsWith("/"))
            {
                url = "/" + url;
            }
            return url;
        }

        protected virtual string GetPageContent(string url)
        {
            string cmUrl = GetCmUrl(url);

            using (new Tracer(url, cmUrl))
            {
                string result;
                PageFactory.TryFindPageContent(GetCmUrl(url), out result);
                return result;
            }
        }

        protected virtual IPage GetPage(string url)
        {
            string cmUrl = GetCmUrl(url);

            using (new Tracer(url, cmUrl))
            {
                IPage result;
                PageFactory.TryFindPage(cmUrl, out result);
                return result;
            }
        }
        
        protected virtual int MapSchema(string schemaKey, Localization localization)
        {
            string[] schemaKeyParts = schemaKey.Split('.');
            string moduleName = schemaKeyParts.Length > 1 ? schemaKeyParts[0] : SiteConfiguration.CoreModuleName;
            schemaKey = schemaKeyParts.Length > 1 ? schemaKeyParts[1] : schemaKeyParts[0];
            string schemaId = localization.GetConfigValue(string.Format("{0}.schemas.{1}", moduleName, schemaKey));

            int result;
            Int32.TryParse(schemaId, out result);
            return result;
        }

        protected virtual IEnumerable<IPage> GetIncludesFromModel(IPage page, Localization localization)
        {
            List<IPage> result = new List<IPage>();
            string[] pageTemplateTcmUriParts = page.PageTemplate.Id.Split('-');
            IEnumerable<string> includePageUrls = SiteConfiguration.GetIncludePageUrls(pageTemplateTcmUriParts[1], localization);
            foreach (string includePageUrl in includePageUrls)
            {
                IPage includePage = GetPage(SiteConfiguration.LocalizeUrl(includePageUrl, localization));
                if (includePage == null)
                {
                    Log.Error("Include Page '{0}' not found.", includePageUrl);
                    continue;
                }
                result.Add(includePage);
            }
            return result;
        }
    }
}