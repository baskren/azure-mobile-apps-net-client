// ----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// ----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.WindowsAzure.MobileServices.Query
{
    /// <summary>
    /// Represents the result of odata query returned from Mobile Service
    /// </summary>
    internal class QueryResult
    {
        /// <summary>
        /// The name of the results key in an inline count response object.
        /// </summary>
        private const string InlineCountResultsKey = "results";

        /// <summary>
        /// The name of the count key in an inline count response object.
        /// </summary>
        private const string InlineCountCountKey = "count";

        /// <summary>
        /// The name of the next link in a response object.
        /// </summary>
        private const string NextLinkKey = "nextLink";

        /// <summary>
        /// The name of the relation for next page link
        /// </summary>
        private const string NextRelation = "next";

        /// <summary>
        /// Count of total rows that match the query without skip and top
        /// </summary>
        public long TotalCount { get; private set; }

        /// <summary>
        /// Items in query result 
        /// </summary>
        public JArray Values { get; private set; }

        /// <summary>
        /// Gets the link to next page of result that is returned in response headers.
        /// </summary>
        public Uri NextLink { get; internal set; }

        /// <summary>
        /// The deserialized response
        /// </summary>
        public JToken Response { get; private set; }

        /// <summary>
        /// Parse a JSON response into <see cref="QueryResult"/> object 
        /// that contains sequence of elements and the count of objects.  
        /// This method abstracts out the differences between a raw array response and 
        /// an inline count response.
        /// </summary>
        /// <param name="httpResponse">
        /// The HTTP response
        /// </param>
        /// <param name="serializerSettings">
        /// The serialization settings
        /// </param>
        /// <param name="validate">
        /// To throw if the content is null or empty
        /// </param>
        public static QueryResult Parse(MobileServiceHttpResponse httpResponse, JsonSerializerSettings serializerSettings, bool validate)
        {
            Arguments.IsNotNull(httpResponse, nameof(httpResponse));

            JToken jtokenResponse = httpResponse.Content.ParseToJToken(serializerSettings);

            JToken response = null;
            Uri link = null;

            // the below was added to allow for the modified form for which the data is recieved from the Proxy
            if (jtokenResponse is JArray array && array.Count == 1)
            {
                jtokenResponse = array[0];
                response = jtokenResponse["value"];
                if (jtokenResponse["@odata.nextLink"] is JToken nextLink && nextLink.Value<string>() is string nv)
                {
                    /*
                    if (!string.IsNullOrWhiteSpace(context))
                    {
                        var trunkIndex = context.IndexOf("/tables");
                        var trunk = context.Substring(0, trunkIndex);
                        var branchIndex = nv.IndexOf("/tables");
                        var branch = nv.Substring(branchIndex);
                        nv = trunk + branch;
                    }
                    */
                    link = new Uri(nv);
                }
            }

            //Uri link = httpResponse.Link != null && httpResponse.Link.Relation == NextRelation ? httpResponse.Link.Uri : null;

            /*
            string context = null;
            if (jtokenResponse["@odata.context"] is JToken contextToken && contextToken.Value<string>() is string ctx)
            {
                context = ctx;
            }
            */

            return Parse(response, link, validate);
        }

        public static QueryResult Parse(JToken response, Uri nextLink, bool validate)
        {
            var result = new QueryResult() { Response = response };

            long? inlineCount = null;

            // Try and get the values as an array
            result.Values = response as JArray;
            if (result.Values == null && response is JObject)
            {
                // Otherwise try and get the values from the results property
                // (which is the case when we retrieve the count inline)
                result.Values = response[InlineCountResultsKey] as JArray;
                inlineCount = response.Value<long?>(InlineCountCountKey);
                if (result.Values == null && validate)
                {
                    string responseStr = response != null ? response.ToString() : "null";
                    throw new InvalidOperationException($"Could not get an array from response {responseStr}.");
                }
                else if (result.Values == null)
                {
                    result.Values = new JArray(response);
                }
            }

            // Get the count via the inline count or default an unspecified count to -1
            result.TotalCount = inlineCount.GetValueOrDefault(-1L);

            result.NextLink = nextLink;

            return result;
        }

        public JObject ToJObject()
        {
            var result = new JObject()
            {
                { InlineCountCountKey, this.TotalCount },
                { InlineCountResultsKey, this.Values },
            };

            if (this.NextLink != null)
            {
                result[NextLinkKey] = this.NextLink.ToString();
            }

            return result;
        }
    }
}
