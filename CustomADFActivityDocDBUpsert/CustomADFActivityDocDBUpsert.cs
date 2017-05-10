using System.IO;
using System.Globalization;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.Azure.Management.DataFactories.Models;
using Microsoft.Azure.Management.DataFactories.Runtime;

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

using System.Net;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace CustomADFActivityDocDBUpsertNS
{
    public class CustomADFActivityDocDBUpsertClass : IDotNetActivity
    {
        public IDictionary<string, string> Execute(
        IEnumerable<LinkedService> linkedServices,
        IEnumerable<Dataset> datasets,
        Activity activity,
        IActivityLogger logger)
        {
            // get extended properties defined in activity JSON definition
            // (for example: SliceStart)
            DotNetActivity dotNetActivity = (DotNetActivity)activity.TypeProperties;
            string sliceStartString = dotNetActivity.ExtendedProperties["SliceStart"];

            // to log information, use the logger object
            // log all extended properties            
            IDictionary<string, string> extendedProperties = dotNetActivity.ExtendedProperties;
            logger.Write("Logging extended properties if any...");
            foreach (KeyValuePair<string, string> entry in extendedProperties)
            {
                logger.Write("<key:{0}> <value:{1}>", entry.Key, entry.Value);
            }

            // linked service for input and output data stores
            AzureStorageLinkedService inputLinkedService;
            DocumentDbLinkedService outputLinkedService;

            // get the input dataset
            Dataset inputDataset = datasets.Single(dataset => dataset.Name == activity.Inputs.Single().Name);
            Dataset outputDataset = datasets.Single(dataset => dataset.Name == activity.Outputs.Single().Name);

            // declare variables to hold type properties of input/output datasets
            AzureBlobDataset inputTypeProperties;
            DocumentDbCollectionDataset outputTypeProperties;

            // get type properties from the dataset object
            inputTypeProperties = inputDataset.Properties.TypeProperties as AzureBlobDataset;

            // log linked services passed in linkedServices parameter
            // you will see two linked services of type: AzureStorage
            // one for input dataset and the other for output dataset 
            foreach (LinkedService ls in linkedServices)
                logger.Write("linkedService.Name {0}", ls.Name);

            // get the first Azure Storate linked service from linkedServices object
            inputLinkedService = linkedServices.First(
                linkedService =>
                linkedService.Name ==
                inputDataset.Properties.LinkedServiceName).Properties.TypeProperties
                as AzureStorageLinkedService;

            outputLinkedService = linkedServices.First(
                linkedService =>
                linkedService.Name ==
                outputDataset.Properties.LinkedServiceName).Properties.TypeProperties
                as DocumentDbLinkedService;

            // get the connection string in the linked service
            string inputConnectionString = inputLinkedService.ConnectionString;
            string outputConnectionString = outputLinkedService.ConnectionString;

            // get the folder path from the input dataset definition
            string folderPath = GetFolderPath(inputDataset);
            string output = string.Empty; // for use later.

            // create storage client for input. Pass the connection string.
            CloudStorageAccount inputStorageAccount = CloudStorageAccount.Parse(inputConnectionString);
            CloudBlobClient inputClient = inputStorageAccount.CreateCloudBlobClient();

            // parse the DocDB connection string
            Dictionary<string, string> outputConnectionStringParts = outputConnectionString.Split(';')
                .Select(t => t.Split(new char[] { '=' }, 2))
                .ToDictionary(t => t[0].Trim(), t => t[1].Trim(), StringComparer.InvariantCultureIgnoreCase);

            
            DocumentClient outputClient = new DocumentClient(new System.Uri(outputConnectionStringParts["accountendpoint"]), outputConnectionStringParts["accountkey"]);

            outputTypeProperties = outputDataset.Properties.TypeProperties as DocumentDbCollectionDataset;
            string outputDatabase = outputConnectionStringParts["database"];
            string outputCollection = outputTypeProperties.CollectionName;

            // initialize the continuation token before using it in the do-while loop.
            BlobContinuationToken continuationToken = null;
            do
            {   // get the list of input blobs from the input storage client object.
                BlobResultSegment blobList = inputClient.ListBlobsSegmented(folderPath,
                                         true,
                                         BlobListingDetails.Metadata,
                                         null,
                                         continuationToken,
                                         null,
                                         null);
                logger.Write("number of blobs found: {0}\n", blobList.Results.Count<IListBlobItem>());

                foreach (IListBlobItem listBlobItem in blobList.Results)
                {
                    dynamic json = ReadBlob(listBlobItem, logger, folderPath, ref continuationToken);
                    foreach (var doc in json)
                    {
                        CreateorReplaceDocument(outputClient, outputDatabase, outputCollection, doc, logger);
                    }
                }

            } while (continuationToken != null);
       
            // The dictionary can be used to chain custom activities together in the future.
            // This feature is not implemented yet, so just return an empty dictionary.  
            return new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets the folderPath value from the input/output dataset.
        /// </summary>
        /// 

        private static string GetFolderPath(Dataset dataArtifact)
        {
            if (dataArtifact == null || dataArtifact.Properties == null)
            {
                return null;
            }

            // get type properties of the dataset   
            AzureBlobDataset blobDataset = dataArtifact.Properties.TypeProperties as AzureBlobDataset;
            if (blobDataset == null)
            {
                return null;
            }

            // return the folder path found in the type properties
            return blobDataset.FolderPath;
        }

        private async Task CreateorReplaceDocument(DocumentClient client, string databaseName, string collectionName, dynamic json, IActivityLogger logger)
        {
            string id = json.id;
            var docExists = client.CreateDocumentQuery(UriFactory.CreateDocumentCollectionUri(databaseName, collectionName))
                    .Where(doc => doc.Id == id)
                    .Select(doc => doc.Id)
                    .AsEnumerable()
                    .Any();

            if (docExists)
            {
                Uri docUri = UriFactory.CreateDocumentUri(databaseName, collectionName, id);
                await client.ReplaceDocumentAsync(docUri, json);
                logger.Write("{0} replaced\n", id);
            }
            //doc does not exist, so create a new document
            else
            {
                Uri collUri = UriFactory.CreateDocumentCollectionUri(databaseName, collectionName);
                await client.CreateDocumentAsync(collUri, json);
                logger.Write("{0} created\n", id);
            }
        }


        /// <summary>
        /// Gets the fileName value from the input/output dataset.   
        /// </summary>

        private static string GetFileName(Dataset dataArtifact)
        {
            if (dataArtifact == null || dataArtifact.Properties == null)
            {
                return null;
            }

            // get type properties of the dataset
            AzureBlobDataset blobDataset = dataArtifact.Properties.TypeProperties as AzureBlobDataset;
            if (blobDataset == null)
            {
                return null;
            }

            // return the blob/file name in the type properties
            return blobDataset.FileName;
        }

        /// <summary>
        /// Iterates through each blob (file) in the folder, counts the number of instances of search term in the file,
        /// and prepares the output text that is written to the output blob.
        /// </summary>

        public dynamic ReadBlob(IListBlobItem listBlobItem, IActivityLogger logger, string folderPath, ref BlobContinuationToken token)
        {
            
            dynamic output = null;

            CloudBlockBlob inputBlob = listBlobItem as CloudBlockBlob;
            if ((inputBlob != null) && (inputBlob.Name.IndexOf("$$$.$$$") == -1))
            {
                string blobText = inputBlob.DownloadText(Encoding.ASCII, null, null, null);
                output = Newtonsoft.Json.JsonConvert.DeserializeObject(blobText);
            }
            
            return output;
        }
    }
}
