﻿using System;
using System.Collections.Generic;
using System.Web.UI;
using umbraco.cms.businesslogic.template;
using System.IO;
using System.Text.RegularExpressions;

namespace Vega.USiteBuilder
{
    internal class TemplateManager : ManagerBase
    {
        private static List<Template> ExistingTemplates = new List<Template>();

        public void Synchronize()
        {
            if (Util.DefaultRenderingEngine == Umbraco.Core.RenderingEngine.WebForms)
            {
                SynchronizeTemplates(typeof(TemplateBase));
                UpdateTemplatesTreeStructure();
            }
            else if (Util.DefaultRenderingEngine == Umbraco.Core.RenderingEngine.Mvc)
            {
                SynchronizeViews();
                UpdateViewsTreeStructure();
            }
        }

        public static string GetTemplateAlias(Type typeTemplate)
        { 
            return typeTemplate.Name;
        }

        public static string GetViewParent(string templateCode)
        {
            string parentMasterPageName = null;
            
            var match = Regex.Match(templateCode, @"Layout\s*\=\s*\""(.*)\""");
            if (match.Success)
            {
                parentMasterPageName = match.Groups[1].Value.Replace(".cshtml", "").Replace(".vbhtml", "");
                string[] pathArray = parentMasterPageName.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (pathArray.Length > 0)
                {
                    parentMasterPageName = pathArray[pathArray.Length - 1];
                }
            }

            return parentMasterPageName;
        }

        private void UpdateViewsTreeStructure()
        {
            List<Template> templates = Template.GetAllAsList();

            foreach (Template template in templates)
            {
                string parentMasterPageName = GetViewParent(template.Design);

                if (!string.IsNullOrEmpty(parentMasterPageName) && parentMasterPageName != "default")
                {
                    Template parentTemplate = templates.Find(tm => tm.Alias == parentMasterPageName);

                    if (parentTemplate == null)
                    {
                        throw new Exception(
                            string.Format(
                                "Template '{0}' is using '{1}' as a parent template (defined in MasterPageFile in {0}.master) but '{1}' template cannot be found",
                                template.Alias, parentMasterPageName));
                    }

                    if (template.MasterTemplate != parentTemplate.Id)
                    {
                        template.MasterTemplate = parentTemplate.Id;
                    }
                }
            }
        }

        private void SynchronizeViews()
        {
            string viewsPath = System.Web.Hosting.HostingEnvironment.MapPath(Umbraco.Core.IO.SystemDirectories.MvcViews);
            if (viewsPath != null)
            {
                DirectoryInfo viewsFolder = new DirectoryInfo(viewsPath);

                foreach (var viewFile in viewsFolder.GetFiles("*.cshtml", SearchOption.TopDirectoryOnly))
                {
                    string alias = Path.GetFileNameWithoutExtension(viewFile.Name).Replace(" ", "");
	                if (String.Equals("_ViewStart", alias, StringComparison.OrdinalIgnoreCase))
	                {
		                continue;
	                }

                    Template template = Template.GetByAlias(alias);
                    if (template == null)
                    {
                        template = Template.MakeNew(alias, siteBuilderUser);
                    }
                }
            }
        }

        private void SynchronizeTemplates(Type typeBaseTemplate)
        {
            // Get list of all templates from Umbraco
            ExistingTemplates = Template.GetAllAsList();

            foreach (Type typeTemplate in Util.GetFirstLevelSubTypes(typeBaseTemplate))
            {
                if (!this.IsBaseTemplate(typeTemplate))
                {
                    this.SynchronizeTemplate(typeTemplate);
                }
                else
                {                    
                    // recursive call (for generic templates)
                    this.SynchronizeTemplates(typeTemplate);
                } 
            }
        }

        private void SynchronizeTemplate(Type typeTemplate)
        {

            string alias = GetTemplateAlias(typeTemplate);
            try
            {
                AddToSynchronized(null, alias, typeTemplate);
            }
            catch (ArgumentException exc)
            {
                throw new Exception(string.Format("Template (masterpage) with alias '{0}' already exists! Please use unique masterpage names as masterpage name is used as alias. Masterpage causing the problem: '{1}' (assembly: '{2}'). Error message: {3}",
                    alias, typeTemplate.FullName, typeTemplate.Assembly.FullName, exc.Message));
            }

            if (!ExistingTemplates.Exists(template => template.Alias == alias))
            {
                Template.MakeNew(alias, this.siteBuilderUser);
            }
        }

        public bool IsBaseTemplate(Type typeTemplate)
        {
            bool retVal = typeTemplate == typeof(TemplateBase) || typeTemplate.IsGenericType || typeTemplate.Namespace == "ASP";

            return retVal;
        }

        private void UpdateTemplatesTreeStructure()
        {
            List<Template> templates = Template.GetAllAsList();

            foreach (Template template in templates)
            {
                string parentMasterPageName = this.GetParentMasterPageName(template);

                if (!string.IsNullOrEmpty(parentMasterPageName) && parentMasterPageName != "default")
                {
                    Template parentTemplate = templates.Find(tm => tm.Alias == parentMasterPageName);

                    if (parentTemplate == null)
                    {
                        throw new Exception(string.Format("Template '{0}' is using '{1}' as a parent template (defined in MasterPageFile in {0}.master) but '{1}' template cannot be found",
                            template.Alias, parentMasterPageName));
                    }
                    if (template.MasterTemplate != parentTemplate.Id)
                    {
                        template.MasterTemplate = parentTemplate.Id;
                    }                    
                }
            }
        }

        public string GetParentMasterPageName(Template template)
        {
            string masterPageContent = File.ReadAllText(template.TemplateFilePath);
            return GetParentMasterPageName(masterPageContent);
        }

        public string GetParentMasterPageName(string masterPageContent)
        {
            string retVal = null;

            string masterHeader =
                masterPageContent.Substring(0, masterPageContent.IndexOf("%>") + 2).Trim(Environment.NewLine.ToCharArray());
            // find the masterpagefile attribute
            MatchCollection m = Regex.Matches(masterHeader, "(?<attributeName>\\S*)=\"(?<attributeValue>[^\"]*)\"",
                                              RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace);

            foreach (Match attributeSet in m)
            {
                if (attributeSet.Groups["attributeName"].Value.ToLower() == "masterpagefile")
                {
                    // validate the masterpagefile
                    string masterPageFile = attributeSet.Groups["attributeValue"].Value;

                    int startIdx = masterPageFile.LastIndexOf("/", StringComparison.CurrentCultureIgnoreCase);
                    if (startIdx < 0)
                    {
                        startIdx = 0;
                    }
                    else
                    {
                        startIdx += 1; // so it won't include '/'
                    }

                    int endIdx = masterPageFile.LastIndexOf(".master", StringComparison.CurrentCultureIgnoreCase);

                    retVal = masterPageFile.Substring(startIdx, endIdx - startIdx);
                }
            }

            return retVal;
        }

        /// <summary>
        /// Returns all templates which are using given document type
        /// </summary>
        public static List<Type> GetAllTemplates(Type typeDocType)
        {
            List<Type> retVal = new List<Type>();

            List<Type> allTemplates = Util.GetAllSubTypes(typeof(TemplateBase));
            foreach (Type typeTemplate in allTemplates)
            {
                if (Util.IsGenericArgumentTypeOf(typeTemplate, typeDocType))
                {
                    // try to get the attribute
                    TemplateAttribute templateAttr = Util.GetAttribute<TemplateAttribute>(typeTemplate);
                    if (templateAttr == null || templateAttr.AllowedForDocumentType)
                    {
                        retVal.Add(typeTemplate);
                    }
                }
            }

            return retVal;
        }
    }
}
