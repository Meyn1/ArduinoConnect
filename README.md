﻿[![NuGet](https://img.shields.io/nuget/vpre/Shard.DonwloadLibrary)](https://www.nuget.org/packages/Shard.DonwloadLibrary) [![Downloads](https://img.shields.io/nuget/dt/Shard.DonwloadLibrary)](https://www.nuget.org/packages/Shard.DonwloadLibrary) [![License](https://img.shields.io/github/license/meyn1/downloadlibrary.svg)](https://github.com/meyn1/downloadlibrary/blob/master/LICENSE)
# Arduino Connect
## _Windows BLE Conector to your Arduino_


The Bluetooth Connect NuGet is an on .Net 6.0 based wrapper around the `Windows.Devices.Bluetooth` to send and recive data from a BLE device like a arduino.
If the arduino supports notification then reciving data will be handeled by an event.

## Features
- Enable and disable Bluetooth.
- Scan for devices.
- Disconnect.
- Send and recive byte arrays
- **Request:** Main abstract class that can be used to expand functionality on class-based level.
    - All subclasses have a retry function
    - A priority function
    - A second thread for bigger files to hold the application responsive
    - Delegates to notify when a `Request` failed, completed, or canceled
    - Implementation for custom `CancellationToken` and a main `CancellationTokenSource` on `Downloader` to cancel all downloads
- **LoadRequest:** To download the response content into files.
  - This is an HTTP file downloader with these functions:
  - *Pause* and *Start* a download
  - *Resume* a download
  - Get the *file name* and *extension* from the server 
  - Timeout function
  - Monitor the progress of the download with `IProgress<float>`
  - Can set path and filename 
  - Download a specified range of a file
  - Download a file into chunks
  - Exclude extensions for safety _(.exe; .bat.; etc...)_

> Expand and use as you like!

## Tech
It is available on GitHub:
Repository: https://github.com/Meyn1/DownloadLibrary

## Installation

Installation over [NuGet](https://www.nuget.org/packages/Shard.DonwloadLibrary) Package manager in Visual Studio or online.
URL: https://www.nuget.org/packages/Shard.DonwloadLibrary.
Package Manager Console: PM> NuGet\Install-Package Shard.DonwloadLibrary

## How to use

Import the Library.
```cs
using DownloaderLibrary.Requests;
```
Then create a new `Request` object like this `LoadRequest`.
This `LoadRequest` downloads a file into the download's folder of the PC with a ".part" file and uses the name that the server provides.
```cs
//To download a file and store it in "Downloads" folder
new LoadRequest("[Your URL]"); // e.g. https://www.sample-videos.com/video123/mkv/240/big_buck_bunny_240p_30mb.mkv
```
To set options on the `Request` create a `RequestOption` or for a `LoadRequest` a `LoadRequestOption`.
```cs
// Create an option for a LoadRequest
  LoadRequestOptions requestOptions = new()
        {
            // Sets the filename of the download without the extension
            // The extension will be added automatically!
            FileName = "downloadfile", 
            // If this download has priority (default is false)
            PriorityLevel = PriorityLevel.High, 
            //(default is download folder)
            Path = "C:\\Users\\[Your Username]\\Desktop", 
            // If this Request contains a heavy request put it in second thread (default is false)
            IsDownload = true,
            //If the downloader should Override, Create a new file or Append (default is Append)
            //Resume function only available with append!
            Mode = LoadMode.Create, 
            // Progress that writes the % to the Console
            Progress = new Progress<float>(f => Console.WriteLine((f).ToString("0.0%"))),
            //Chunk a file to download faster
            Chunks = 3
        };
```
And use it in the request.
```cs
//To download a file and store it on the Desktop with a different name
new LoadRequest(" https://speed.hetzner.de/100MB.bin",requestOptions);
```
To wait on the request, use *await* or *WaitToFinish();*.
```cs
await new LoadRequest(" https://speed.hetzner.de/100MB.bin",requestOptions).Task;
//new LoadRequest(" https://speed.hetzner.de/100MB.bin",requestOptions).WaitToFinish();
```
Create an `OwnRequest` like this:
```cs
    //Create an object that passes a CancellationToken
   new OwnRequest((downloadToken) =>
        {
            //Create your request Message. Here the body of google.com
            HttpRequestMessage requestMessage = new(HttpMethod.Get, "https://www.google.com");
            //Send your request and get the result. Pass the CancellationToken for handling it later over the Request object
            var response = DownloadHandler.Instance.SendAsync(requestMessage, downloadToken).Result;
            //If the response does not succeed
            if (!response.IsSuccessStatusCode)
                return false; // Return false to retry and call the failed method
            //If the response succeed. Do what you want and return to to finish the request
            Console.WriteLine("Finished");
            return true;
        });
```
To create your own `Request` child. Here is the implementation of the `OwnRequest` class:
```cs
    public class OwnRequest : Request
    {
        private readonly Func<CancellationToken, Task<bool>> _own;
        
        //Parent sets the URL field but doesn't need it and doesn't require a RequestOption because it creates then a new one.
        //But to use the options it have to be passed over to the parent
        public OwnRequest(Func<CancellationToken, Task<bool>> own, RequestOptions? requestOptions = null) : base(string.Empty, requestOptions)
        {
            _own = own;
            //Has to be called to inject it into the management process
            Start();
        }
        
        // Here will the Request be handled and a bool returned that indicates if it succeed
        protected override async Task<bool> RunRequestAsync()
        {
            bool result = await _own.Invoke(Token);
            if (result)
                Options.CompleatedAction?.Invoke(null);
            else
                Options.FaultedAction?.Invoke(new HttpResponseMessage());
            return result;
        }
    }
```
## License

MIT

## **Free Code** and **Free to Use**
#### Have fun!
