# Sample - Azure Data Factory Upsert to Document DB

A sample project to demonstrate how one can implement the upsert logic (strictly speaking, since Document DB does not support partial updates, the *insert or replace* logic) using Azure Data Factory with a custom C# activity running on an auto-scaling pool of VMs inside Azure Batch. Inspired by Microsoft's [howto](https://docs.microsoft.com/en-us/azure/data-factory/data-factory-use-custom-activities) and  the [solution for local debugging](https://blog.gbrueckl.at/2016/11/debugging-custom-net-activities-azure-data-factory/) of custom C# activities. 

## Solution Components
1. Azure Data Factory
1. Azure Batch
1. C# custom activity implementing the Insert or Replace logic
1. Input: Blob Storage Container
1. Blob Storage Container for compiled C# code
1. Output: DocumentDB Collection

## Provisioning
Manually, had no time to create an ARM template:
1. Download the latest Azure Data Factory [plugin](https://marketplace.visualstudio.com/items?itemName=AzureDataFactory.MicrosoftAzureDataFactoryToolsforVisualStudio2015) for Visual Studio
1. Provision a DocDB database and create a collection
1. Create a blob storage container for source data, you may upload a [sample json](./sample-input)
1. Create a blob storage container for the custom activity
1. Create an Azure Batch pool, you may choose to use the [scaling formula](#azure-batch-scaling)
1. Fire up the solution in VS2015/2017
1. Adjust the ADF json config files in the [DataFactory](./DataFactory) project
   * edit the linked service json files replacing all occurences of `***` with your values
   * adjust the input/output datasets if your input data is time/date partitioned
   * adjust the pipeline definition json as required
1. you can debug the solution locally (launch the [console app](./LocalTest)) or alternatively compile the [custom C# script](./CustomADFActivityDocDBUpsert), zip up all the binaries in the \bin\Debug folder [as described here](https://docs.microsoft.com/en-us/azure/data-factory/data-factory-use-custom-activities#walkthrough) and upload the zip file to the blob storage container specified in the pipeline definition json
1. deploy the ADF, fire up the pipeline, observe jobs being created on the Azure Batch Pool, new VM being spun up automatically and magic happening :-)

## Azure Batch Scaling
The Azure Batch pool used the following auto-scaling function:
```
startingNumberOfVMs = 0;
maxNumberofVMs = 5;
pendingTaskSamplePercent = $PendingTasks.GetSamplePercent(180 * TimeInterval_Second);
pendingTaskSamples = pendingTaskSamplePercent < 70 ? startingNumberOfVMs : avg($PendingTasks.GetSample(180 * TimeInterval_Second));
$TargetDedicated=min(maxNumberofVMs, pendingTaskSamples);
``` 

## Caveats
1. The C# code assumes all input files are in the `arrayOfObjects` JSON format, the `filePattern` setting is ignored - feel free to implement it yourself
2. The ADF setup code does not implement slices - each invokation of the pipeline will re-read the same input file over and over again. Please adjust dataset definitions accordingly
3. Code can be further optimised

