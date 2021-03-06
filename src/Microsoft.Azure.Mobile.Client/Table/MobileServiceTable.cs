﻿// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.MobileServices.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.WindowsAzure.MobileServices
{
    /// <summary>
    /// Provides operations on tables for a Microsoft Azure Mobile Service.
    /// </summary>
    internal class MobileServiceTable : IMobileServiceTable
    {
        /// <summary>
        /// The route separator used to denote the table in a uri like
        /// .../{app}/tables/{coll}.
        /// </summary>
        internal const string TableRouteSeparatorName = "tables";

        /// <summary>
        /// The HTTP PATCH method used for update operations.
        /// </summary>
        private static readonly HttpMethod patchHttpMethod = new HttpMethod("PATCH");

        /// <summary>
        /// The name of the include deleted query string parameter
        /// </summary>
        public const string IncludeDeletedParameterName = "__includeDeleted";

        /// <summary>
        /// Gets a reference to the <see cref="MobileServiceClient"/> associated 
        /// with this table.
        /// </summary>
        public MobileServiceClient MobileServiceClient { get; private set; }

        /// <summary>
        /// Gets the name of the table.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// Feature which are sent as telemetry information to the service for all
        /// outgoing calls.
        /// </summary>
        internal MobileServiceFeatures Features { get; set; }

        /// <summary>
        /// Initializes a new instance of the MobileServiceTable class.
        /// </summary>
        /// <param name="tableName">
        /// The name of the table.
        /// </param>
        /// <param name="client">
        /// The <see cref="MobileServiceClient"/> associated with this table.
        /// </param>
        public MobileServiceTable(string tableName, MobileServiceClient client)
        {
            Debug.Assert(tableName != null);
            Debug.Assert(client != null);

            TableName = tableName;
            MobileServiceClient = client;
        }

        /// <summary>
        /// Executes a query against the table.
        /// </summary>
        /// <param name="query">
        /// A query to execute.
        /// </param>
        /// <returns>
        /// A task that will return with results when the query finishes.
        /// </returns>
        public virtual Task<JToken> ReadAsync(string query) => ReadAsync(query, null, wrapResult: false);

        /// <summary>
        /// Executes a query against the table.
        /// </summary>
        /// <param name="query">
        /// A query to execute.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="wrapResult">
        /// Specifies whether response should be formatted as JObject including extra response details e.g. link header
        /// </param>
        /// <returns>
        /// A task that will return with results when the query finishes.
        /// </returns>
        public virtual async Task<JToken> ReadAsync(string query, IDictionary<string, string> parameters, bool wrapResult)
        {
            QueryResult result = await this.ReadAsync(query, parameters, MobileServiceFeatures.UntypedTable);
            return wrapResult ? result.ToJObject() : result.Response;
        }

        /// <summary>
        /// Executes a query against the table.
        /// </summary>
        /// <param name="query">
        /// A query to execute.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="features">
        /// Value indicating which features of the SDK are being used in this call. Useful for telemetry.
        /// </param>
        /// <returns>
        /// A task that will return with results when the query finishes.
        /// </returns>
        internal virtual async Task<QueryResult> ReadAsync(string query, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            features = AddRequestFeatures(features, parameters);

            string uriPath;
            if (HttpUtility.TryParseQueryUri(this.MobileServiceClient.MobileAppUri, query, out Uri uri, out bool absolute))
            {
                if (absolute)
                {
                    features |= MobileServiceFeatures.ReadWithLinkHeader;
                }
                uriPath = HttpUtility.GetUriWithoutQuery(uri);
                query = uri.Query;
            }
            else
            {
                uriPath = MobileServiceUrlBuilder.CombinePaths(TableRouteSeparatorName, this.TableName);
            }

            string parametersString = MobileServiceUrlBuilder.GetQueryString(parameters);

            // Concatenate the query and the user-defined query string parameters
            if (!string.IsNullOrEmpty(parametersString))
            {
                if (!string.IsNullOrEmpty(query))
                {
                    query += '&' + parametersString;
                }
                else
                {
                    query = parametersString;
                }
            }

            string uriString = MobileServiceUrlBuilder.CombinePathAndQuery(uriPath, query);

            return await ReadAsync(uriString, features);
        }

        internal Task<QueryResult> ReadAsync(Uri uri) => ReadAsync(uri.ToString(), this.Features);

        private async Task<QueryResult> ReadAsync(string uriString, MobileServiceFeatures features)
        {
            /*
            MobileServiceHttpResponse response = await MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Get, uriString, MobileServiceClient.CurrentUser, null, true, features: Features | features);
            return QueryResult.Parse(response, MobileServiceClient.SerializerSettings, validate: false);
            */
            MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Get, uriString, this.MobileServiceClient.CurrentUser, null, true, features: this.Features | features);


            var result = QueryResult.Parse(response, this.MobileServiceClient.SerializerSettings, validate: false);
            /*
            if (uriString.Contains("&$top=") && result.Values != null && result.Values.Count > 0)
            {
                var parameters = uriString.Split("&");

                if (parameters.FirstOrDefault(s => s.StartsWith("$skip=", StringComparison.OrdinalIgnoreCase)) is string skipParameter
                    && int.TryParse(skipParameter.Substring(6), out int skip))
                {
                    if (parameters.FirstOrDefault(s => s.StartsWith("$top=", StringComparison.OrdinalIgnoreCase)) is string topParameter
                        && int.TryParse(topParameter.Substring(5), out int top))
                    {
                        var delta = top - skip;

                        if (result.Values.Count >= delta)
                        {
                            List<string> linkParams = new List<string>();
                            foreach (var parameter in parameters)
                            {
                                if (parameter == skipParameter)
                                    linkParams.Add("$skip=" + (skip + delta));
                                else if (parameter == topParameter)
                                    linkParams.Add("$top=" + delta);
                                else
                                    linkParams.Add(parameter);
                            }

                            var nextLink = string.Join("&", linkParams);
                            var nextLinkUri = new Uri(this.MobileServiceClient.HttpClient.applicationUri, nextLink);

                            result.NextLink = nextLinkUri;
                        }
                    }
                }
            }
            */


            return result;
        }

        /// <summary>
        /// Inserts an <paramref name="instance"/> into the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to insert into the table.
        /// </param>
        /// <returns>
        /// A task that will complete when the insert finishes.
        /// </returns>
        public virtual Task<JToken> InsertAsync(JObject instance) => InsertAsync(instance, null);

        /// <summary>
        /// Inserts an <paramref name="instance"/> into the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to insert into the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>
        /// A task that will complete when the insert finishes.
        /// </returns>
        public Task<JToken> InsertAsync(JObject instance, IDictionary<string, string> parameters)
            => InsertAsync(instance, parameters, MobileServiceFeatures.UntypedTable);

        /// <summary>
        /// Inserts an <paramref name="instance"/> into the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to insert into the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="features">
        /// Value indicating which features of the SDK are being used in this call. Useful for telemetry.
        /// </param>
        /// <returns>
        /// A task that will complete when the insert finishes.
        /// </returns>
        internal async Task<JToken> InsertAsync(JObject instance, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            Arguments.IsNotNull(instance, nameof(instance));

            // Make sure the instance doesn't have an int id set for an insertion
            object id = MobileServiceSerializer.GetId(instance, ignoreCase: false, allowDefault: true);
            bool isStringIdOrDefaultIntId = id is string || MobileServiceSerializer.IsDefaultId(id);
            if (!isStringIdOrDefaultIntId)
            {
                throw new ArgumentException($"Cannot insert if the {MobileServiceSystemColumns.Id} member is already set.", nameof(instance));
            }

            features = this.AddRequestFeatures(features, parameters);
            string uriString = GetUri(this.TableName, null, parameters);

            return await this.TransformHttpException(async () =>
            {
                MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Post, uriString, this.MobileServiceClient.CurrentUser, instance.ToString(Formatting.None), true, features: this.Features | features);
                return GetJTokenFromResponse(response);
            });
        }


        /// <summary>
        /// Updates an <paramref name="instance"/> in the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to update in the table.
        /// </param>
        /// <returns>
        /// A task that will complete when the update finishes.
        /// </returns>
        public virtual Task<JToken> UpdateAsync(JObject instance) => UpdateAsync(instance, null);

        /// <summary>
        /// Updates an <paramref name="instance"/> in the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to update in the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>
        /// A task that will complete when the update finishes.
        /// </returns>
        public Task<JToken> UpdateAsync(JObject instance, IDictionary<string, string> parameters)
            => UpdateAsync(instance, parameters, MobileServiceFeatures.UntypedTable);

        /// <summary>
        /// Updates an <paramref name="instance"/> in the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to update in the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="features">
        /// Value indicating which features of the SDK are being used in this call. Useful for telemetry.
        /// </param>
        /// <returns>
        /// A task that will complete when the update finishes.
        /// </returns>
        internal async Task<JToken> UpdateAsync(JObject instance, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            Arguments.IsNotNull(instance, nameof(instance));

            features = AddRequestFeatures(features, parameters);
            object id = MobileServiceSerializer.GetId(instance);
            Dictionary<string, string> headers = StripSystemPropertiesAndAddVersionHeader(ref instance, ref parameters, id);
            string content = instance.ToString(Formatting.None);
            string uriString = GetUri(this.TableName, id, parameters);

            return await this.TransformHttpException(async () =>
            {
                MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(patchHttpMethod, uriString, this.MobileServiceClient.CurrentUser, content, true, headers, this.Features | features);
                return GetJTokenFromResponse(response);
            });
        }

        /// <summary>
        /// Undeletes an <paramref name="instance"/> from the table.
        /// </summary>
        /// <param name="instance">The instance to undelete from the table.</param>
        /// <returns>A task that will complete when the undelete finishes.</returns>
        public Task<JToken> UndeleteAsync(JObject instance) => UndeleteAsync(instance, null);

        /// <summary>
        /// Undeletes an <paramref name="instance"/> from the table.
        /// </summary>
        /// <param name="instance">The instance to undelete from the table.</param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>A task that will complete when the undelete finishes.</returns>
        public Task<JToken> UndeleteAsync(JObject instance, IDictionary<string, string> parameters)
            => UndeleteAsync(instance, parameters, MobileServiceFeatures.UntypedTable);

        protected async Task<JToken> UndeleteAsync(JObject instance, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            Arguments.IsNotNull(instance, nameof(instance));

            object id = MobileServiceSerializer.GetId(instance);
            Dictionary<string, string> headers = StripSystemPropertiesAndAddVersionHeader(ref instance, ref parameters, id);
            string content = instance.ToString(Formatting.None);
            string uriString = GetUri(this.TableName, id, parameters);

            return await this.TransformHttpException(async () =>
            {
                MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Post, uriString, this.MobileServiceClient.CurrentUser, null, true, headers, this.Features | features);
                return GetJTokenFromResponse(response);
            });
        }

        /// <summary>
        /// Deletes an <paramref name="instance"/> from the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to delete from the table.
        /// </param>
        /// <returns>
        /// A task that will complete when the delete finishes.
        /// </returns>
        public virtual Task<JToken> DeleteAsync(JObject instance) => DeleteAsync(instance, null);

        /// <summary>
        /// Deletes an <paramref name="instance"/> from the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to delete from the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>
        /// A task that will complete when the delete finishes.
        /// </returns>
        public Task<JToken> DeleteAsync(JObject instance, IDictionary<string, string> parameters)
            => DeleteAsync(instance, parameters, MobileServiceFeatures.UntypedTable);

        /// <summary>
        /// Deletes an <paramref name="instance"/> from the table.
        /// </summary>
        /// <param name="instance">
        /// The instance to delete from the table.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="features">
        /// Value indicating which features of the SDK are being used in this call. Useful for telemetry.
        /// </param>
        /// <returns>
        /// A task that will complete when the delete finishes.
        /// </returns>
        internal async Task<JToken> DeleteAsync(JObject instance, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            Arguments.IsNotNull(instance, nameof(instance));

            object id = MobileServiceSerializer.GetId(instance);
            features = this.AddRequestFeatures(features, parameters);
            Dictionary<string, string> headers = StripSystemPropertiesAndAddVersionHeader(ref instance, ref parameters, id);
            string uriString = GetUri(this.TableName, id, parameters);

            return await TransformHttpException(async () =>
            {
                MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Delete, uriString, this.MobileServiceClient.CurrentUser, null, false, headers, this.Features | features);
                return GetJTokenFromResponse(response);
            });
        }

        /// <summary>
        /// Executes a lookup against a table.
        /// </summary>
        /// <param name="id">
        /// The id of the instance to lookup.
        /// </param>
        /// <returns>
        /// A task that will return with a result when the lookup finishes.
        /// </returns>
        public Task<JToken> LookupAsync(object id) => LookupAsync(id, null);

        /// <summary>
        /// Executes a lookup against a table.
        /// </summary>
        /// <param name="id">
        /// The id of the instance to lookup.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>
        /// A task that will return with a result when the lookup finishes.
        /// </returns>
        public Task<JToken> LookupAsync(object id, IDictionary<string, string> parameters)
            => LookupAsync(id, parameters, MobileServiceFeatures.UntypedTable);

        /// <summary>
        /// Executes a lookup against a table.
        /// </summary>
        /// <param name="id">
        /// The id of the instance to lookup.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <param name="features">
        /// Value indicating which features of the SDK are being used in this call. Useful for telemetry.
        /// </param>
        /// <returns>
        /// A task that will return with a result when the lookup finishes.
        /// </returns>
        internal async Task<JToken> LookupAsync(object id, IDictionary<string, string> parameters, MobileServiceFeatures features)
        {
            MobileServiceSerializer.EnsureValidId(id);

            features = AddRequestFeatures(features, parameters);
            string uriString = GetUri(this.TableName, id, parameters);
            MobileServiceHttpResponse response = await this.MobileServiceClient.HttpClient.RequestAsync(HttpMethod.Get, uriString, this.MobileServiceClient.CurrentUser, null, true, features: this.Features | features);
            return GetJTokenFromResponse(response);
        }

        /// <summary>
        /// Adds the query string parameter to include deleted records.
        /// </summary>
        /// <param name="parameters">The parameters collection.</param>
        /// <returns>
        /// The parameters collection with includeDeleted parameter included.
        /// </returns>
        internal static IDictionary<string, string> IncludeDeleted(IDictionary<string, string> parameters)
            => AddSystemParameter(parameters, MobileServiceTable.IncludeDeletedParameterName, "true");

        /// <summary>
        /// Adds the system parameter to the parameters collection.
        /// </summary>
        /// <param name="parameters">The parameters collection.</param>
        /// <param name="name">The name of system parameter.</param>
        /// <param name="value">The value of system parameter.</param>
        /// <returns></returns>
        internal static IDictionary<string, string> AddSystemParameter(IDictionary<string, string> parameters, string name, string value)
        {
            // Make sure we have a case-insensitive parameters dictionary
            if (parameters != null)
            {
                parameters = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
            }

            // If there is already a user parameter for the system properties, just use it
            if (parameters == null || !parameters.ContainsKey(name))
            {
                if (value != null)
                {
                    parameters ??= new Dictionary<string, string>();
                    parameters.Add(name, value);
                }
            }

            return parameters;
        }

        /// <summary>
        /// Parses body of the <paramref name="response"/> as JToken
        /// </summary>
        /// <param name="response">The http response message.</param>
        /// <returns>A pair of raw response and parsed JToken</returns>
        internal Task<Tuple<string, JToken>> ParseContent(HttpResponseMessage response)
            => ParseContent(response, this.MobileServiceClient.SerializerSettings);

        internal static async Task<Tuple<string, JToken>> ParseContent(HttpResponseMessage response, JsonSerializerSettings serializerSettings)
        {
            string content = null;
            JToken value = null;
            try
            {
                if (response.Content != null)
                {
                    content = await response.Content.ReadAsStringAsync();
                    value = content.ParseToJToken(serializerSettings);
                }
            }
            catch { }
            return Tuple.Create(content, value);
        }

        /// <summary>
        /// Returns a URI for the table, optional id and parameters.
        /// </summary>
        /// <param name="tableName">
        /// The name of the table.
        /// </param>
        /// <param name="id">
        /// The id of the instance.
        /// </param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in 
        /// the request URI query string.
        /// </param>
        /// <returns>
        /// A URI string.
        /// </returns>
        private static string GetUri(string tableName, object id = null, IDictionary<string, string> parameters = null)
        {
            Arguments.IsNotNullOrEmpty(tableName, nameof(tableName));

            string uriPath = MobileServiceUrlBuilder.CombinePaths(TableRouteSeparatorName, tableName);
            if (id != null)
            {
                string idString = Uri.EscapeDataString(string.Format(CultureInfo.InvariantCulture, "{0}", id));
                uriPath = MobileServiceUrlBuilder.CombinePaths(uriPath, idString);
            }

            string queryString = MobileServiceUrlBuilder.GetQueryString(parameters);

            return MobileServiceUrlBuilder.CombinePathAndQuery(uriPath, queryString);
        }

        /// <summary>
        /// Parses the response content into a JToken and adds the version system property
        /// if the ETag was returned from the server.
        /// </summary>
        /// <param name="response">The response to parse.</param>
        /// <returns>The parsed JToken.</returns>
        private JToken GetJTokenFromResponse(MobileServiceHttpResponse response)
        {
            JToken jtoken = response.Content.ParseToJToken(this.MobileServiceClient.SerializerSettings);
            if (response.Etag != null)
            {
                jtoken[MobileServiceSystemColumns.Version] = GetValueFromEtag(response.Etag);
            }

            return jtoken;
        }

        /// <summary>
        /// Adds, if applicable, the <see cref="MobileServiceFeatures.AdditionalQueryParameters"/> value to the
        /// existing list of features used in the current operation, as well as any features set for the
        /// entire table.
        /// </summary>
        /// <param name="existingFeatures">The features from the SDK being used for the current operation.</param>
        /// <param name="parameters">
        /// A dictionary of user-defined parameters and values to include in
        /// the request URI query string.
        /// </param>
        /// <returns>The features used in the current operation.</returns>
        private MobileServiceFeatures AddRequestFeatures(MobileServiceFeatures existingFeatures, IDictionary<string, string> parameters)
        {
            if (parameters != null && parameters.Count > 0)
            {
                existingFeatures |= MobileServiceFeatures.AdditionalQueryParameters;
            }

            existingFeatures |= this.Features;

            return existingFeatures;
        }

        /// <summary>
        /// Executes a request and transforms a 412 and 409 response to respective exception type.
        /// </summary>
        private async Task<JToken> TransformHttpException(Func<Task<JToken>> action)
        {
            MobileServiceInvalidOperationException error;
            try
            {
                return await action();
            }
            catch (MobileServiceInvalidOperationException ex)
            {
                if (ex.Response != null &&
                    ex.Response.StatusCode != HttpStatusCode.PreconditionFailed &&
                    ex.Response.StatusCode != HttpStatusCode.Conflict)
                {
                    throw;
                }

                error = ex;
            }

            Tuple<string, JToken> responseContent = await this.ParseContent(error.Response);
            JObject value = responseContent.Item2.ValidItemOrNull();
            if (error.Response.StatusCode == HttpStatusCode.Conflict)
            {
                error = new MobileServiceConflictException(error, value);
            }
            else if (error.Response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                error = new MobileServicePreconditionFailedException(error, value);
            }
            throw error;
        }

        /// <summary>
        /// if id is of type string then it strips the system properties and adds version header.
        /// </summary>
        /// <returns>The header collection with if-match header.</returns>
        private Dictionary<string, string> StripSystemPropertiesAndAddVersionHeader(ref JObject instance, ref IDictionary<string, string> parameters, object id)
        {
            instance = MobileServiceSerializer.RemoveSystemProperties(instance, out string version);
            Dictionary<string, string> headers = AddIfMatchHeader(version);
            return headers;
        }

        /// <summary>
        /// Adds If-Match header to request if version is non-null.
        /// </summary>
        private static Dictionary<string, string> AddIfMatchHeader(string version)
        {
            Dictionary<string, string> headers = null;
            if (!String.IsNullOrEmpty(version))
            {
                headers = new Dictionary<string, string>
                {
                    ["If-Match"] = GetEtagFromValue(version)
                };
            }

            return headers;
        }

        /// <summary>
        /// Gets a valid etag from a string value. Etags are surrounded
        /// by double quotes and any internal quotes must be escaped with a 
        /// '\'.
        /// </summary>
        /// <param name="value">The value to create the etag from.</param>
        /// <returns>
        /// The etag.
        /// </returns>
        private static string GetEtagFromValue(string value)
        {
            // If the value has double quotes, they will need to be escaped.
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '"' && (i == 0 || value[i - 1] != '\\'))
                {
                    value = value.Insert(i, "\\");
                }
            }

            // All etags are quoted;
            return string.Format("\"{0}\"", value);
        }

        /// <summary>
        /// Gets a value from an etag. Etags are surrounded
        /// by double quotes and any internal quotes must be escaped with a 
        /// '\'.
        /// </summary>
        /// <param name="etag">The etag to get the value from.</param>
        /// <returns>
        /// The value.
        /// </returns>
        private static string GetValueFromEtag(string etag)
        {
            int length = etag.Length;
            if (length > 1 && etag[0] == '\"' && etag[length - 1] == '\"')
            {
                etag = etag.Substring(1, length - 2);
            }

            return etag.Replace("\\\"", "\"");
        }
    }
}
