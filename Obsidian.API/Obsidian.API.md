<a name='assembly'></a>
# Obsidian.API

## Contents

- [AliasAttribute](#T-Obsidian-API-Plugins-AliasAttribute 'Obsidian.API.Plugins.AliasAttribute')
  - [#ctor(identifier)](#M-Obsidian-API-Plugins-AliasAttribute-#ctor-System-String- 'Obsidian.API.Plugins.AliasAttribute.#ctor(System.String)')
  - [Identifier](#P-Obsidian-API-Plugins-AliasAttribute-Identifier 'Obsidian.API.Plugins.AliasAttribute.Identifier')
- [Angle](#T-Obsidian-API-Angle 'Obsidian.API.Angle')
- [AsyncEvent](#T-Obsidian-API-Events-AsyncEvent 'Obsidian.API.Events.AsyncEvent')
- [AsyncEventArgs](#T-Obsidian-API-Events-AsyncEventArgs 'Obsidian.API.Events.AsyncEventArgs')
  - [Handled](#P-Obsidian-API-Events-AsyncEventArgs-Handled 'Obsidian.API.Events.AsyncEventArgs.Handled')
- [AsyncEventHandler](#T-Obsidian-API-Events-AsyncEventHandler 'Obsidian.API.Events.AsyncEventHandler')
- [AsyncEventHandler\`1](#T-Obsidian-API-Events-AsyncEventHandler`1 'Obsidian.API.Events.AsyncEventHandler`1')
- [AsyncEvent\`1](#T-Obsidian-API-Events-AsyncEvent`1 'Obsidian.API.Events.AsyncEvent`1')
- [BaseMinecraftEventArgs](#T-Obsidian-API-Events-BaseMinecraftEventArgs 'Obsidian.API.Events.BaseMinecraftEventArgs')
  - [Server](#P-Obsidian-API-Events-BaseMinecraftEventArgs-Server 'Obsidian.API.Events.BaseMinecraftEventArgs.Server')
- [BlockFace](#T-Obsidian-API-BlockFace 'Obsidian.API.BlockFace')
  - [Bottom](#F-Obsidian-API-BlockFace-Bottom 'Obsidian.API.BlockFace.Bottom')
  - [East](#F-Obsidian-API-BlockFace-East 'Obsidian.API.BlockFace.East')
  - [North](#F-Obsidian-API-BlockFace-North 'Obsidian.API.BlockFace.North')
  - [South](#F-Obsidian-API-BlockFace-South 'Obsidian.API.BlockFace.South')
  - [Top](#F-Obsidian-API-BlockFace-Top 'Obsidian.API.BlockFace.Top')
  - [West](#F-Obsidian-API-BlockFace-West 'Obsidian.API.BlockFace.West')
- [Command](#T-Obsidian-CommandFramework-Entities-Command 'Obsidian.CommandFramework.Entities.Command')
  - [ExecuteAsync(Context)](#M-Obsidian-CommandFramework-Entities-Command-ExecuteAsync-Obsidian-CommandFramework-ObsidianContext,System-String[]- 'Obsidian.CommandFramework.Entities.Command.ExecuteAsync(Obsidian.CommandFramework.ObsidianContext,System.String[])')
  - [GetQualifiedName()](#M-Obsidian-CommandFramework-Entities-Command-GetQualifiedName 'Obsidian.CommandFramework.Entities.Command.GetQualifiedName')
- [DependencyAttribute](#T-Obsidian-API-Plugins-DependencyAttribute 'Obsidian.API.Plugins.DependencyAttribute')
  - [MinVersion](#P-Obsidian-API-Plugins-DependencyAttribute-MinVersion 'Obsidian.API.Plugins.DependencyAttribute.MinVersion')
  - [Optional](#P-Obsidian-API-Plugins-DependencyAttribute-Optional 'Obsidian.API.Plugins.DependencyAttribute.Optional')
  - [GetMinVersion()](#M-Obsidian-API-Plugins-DependencyAttribute-GetMinVersion 'Obsidian.API.Plugins.DependencyAttribute.GetMinVersion')
- [EClickAction](#T-Obsidian-API-EClickAction 'Obsidian.API.EClickAction')
  - [ChangePage](#F-Obsidian-API-EClickAction-ChangePage 'Obsidian.API.EClickAction.ChangePage')
  - [CopyToClipboard](#F-Obsidian-API-EClickAction-CopyToClipboard 'Obsidian.API.EClickAction.CopyToClipboard')
  - [OpenUrl](#F-Obsidian-API-EClickAction-OpenUrl 'Obsidian.API.EClickAction.OpenUrl')
  - [RunCommand](#F-Obsidian-API-EClickAction-RunCommand 'Obsidian.API.EClickAction.RunCommand')
  - [SuggestCommand](#F-Obsidian-API-EClickAction-SuggestCommand 'Obsidian.API.EClickAction.SuggestCommand')
- [EHoverAction](#T-Obsidian-API-EHoverAction 'Obsidian.API.EHoverAction')
  - [ShowEntity](#F-Obsidian-API-EHoverAction-ShowEntity 'Obsidian.API.EHoverAction.ShowEntity')
  - [ShowItem](#F-Obsidian-API-EHoverAction-ShowItem 'Obsidian.API.EHoverAction.ShowItem')
  - [ShowText](#F-Obsidian-API-EHoverAction-ShowText 'Obsidian.API.EHoverAction.ShowText')
- [IConfig](#T-Obsidian-API-IConfig 'Obsidian.API.IConfig')
  - [DownloadPlugins](#P-Obsidian-API-IConfig-DownloadPlugins 'Obsidian.API.IConfig.DownloadPlugins')
  - [Footer](#P-Obsidian-API-IConfig-Footer 'Obsidian.API.IConfig.Footer')
  - [Generator](#P-Obsidian-API-IConfig-Generator 'Obsidian.API.IConfig.Generator')
  - [Header](#P-Obsidian-API-IConfig-Header 'Obsidian.API.IConfig.Header')
  - [JoinMessage](#P-Obsidian-API-IConfig-JoinMessage 'Obsidian.API.IConfig.JoinMessage')
  - [LeaveMessage](#P-Obsidian-API-IConfig-LeaveMessage 'Obsidian.API.IConfig.LeaveMessage')
  - [MaxMissedKeepAlives](#P-Obsidian-API-IConfig-MaxMissedKeepAlives 'Obsidian.API.IConfig.MaxMissedKeepAlives')
  - [MaxPlayers](#P-Obsidian-API-IConfig-MaxPlayers 'Obsidian.API.IConfig.MaxPlayers')
  - [Motd](#P-Obsidian-API-IConfig-Motd 'Obsidian.API.IConfig.Motd')
  - [MulitplayerDebugMode](#P-Obsidian-API-IConfig-MulitplayerDebugMode 'Obsidian.API.IConfig.MulitplayerDebugMode')
  - [OnlineMode](#P-Obsidian-API-IConfig-OnlineMode 'Obsidian.API.IConfig.OnlineMode')
  - [Port](#P-Obsidian-API-IConfig-Port 'Obsidian.API.IConfig.Port')
  - [Seed](#P-Obsidian-API-IConfig-Seed 'Obsidian.API.IConfig.Seed')
- [IDiagnoser](#T-Obsidian-API-Plugins-Services-IDiagnoser 'Obsidian.API.Plugins.Services.IDiagnoser')
  - [GetProcess()](#M-Obsidian-API-Plugins-Services-IDiagnoser-GetProcess 'Obsidian.API.Plugins.Services.IDiagnoser.GetProcess')
  - [GetProcesses()](#M-Obsidian-API-Plugins-Services-IDiagnoser-GetProcesses 'Obsidian.API.Plugins.Services.IDiagnoser.GetProcesses')
  - [GetStopwatch()](#M-Obsidian-API-Plugins-Services-IDiagnoser-GetStopwatch 'Obsidian.API.Plugins.Services.IDiagnoser.GetStopwatch')
  - [StartProcess(fileName,arguments,createWindow,useShell)](#M-Obsidian-API-Plugins-Services-IDiagnoser-StartProcess-System-String,System-String,System-Boolean,System-Boolean- 'Obsidian.API.Plugins.Services.IDiagnoser.StartProcess(System.String,System.String,System.Boolean,System.Boolean)')
  - [StartStopwatch()](#M-Obsidian-API-Plugins-Services-IDiagnoser-StartStopwatch 'Obsidian.API.Plugins.Services.IDiagnoser.StartStopwatch')
- [IFileReader](#T-Obsidian-API-Plugins-Services-IFileReader 'Obsidian.API.Plugins.Services.IFileReader')
  - [GetDirectoryFiles()](#M-Obsidian-API-Plugins-Services-IFileReader-GetDirectoryFiles-System-String- 'Obsidian.API.Plugins.Services.IFileReader.GetDirectoryFiles(System.String)')
  - [GetSubdirectories()](#M-Obsidian-API-Plugins-Services-IFileReader-GetSubdirectories-System-String- 'Obsidian.API.Plugins.Services.IFileReader.GetSubdirectories(System.String)')
  - [OpenRead()](#M-Obsidian-API-Plugins-Services-IFileReader-OpenRead-System-String- 'Obsidian.API.Plugins.Services.IFileReader.OpenRead(System.String)')
  - [ReadAllBytes()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllBytes-System-String- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllBytes(System.String)')
  - [ReadAllBytesAsync()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllBytesAsync-System-String,System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllBytesAsync(System.String,System.Threading.CancellationToken)')
  - [ReadAllLines()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllLines-System-String- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllLines(System.String)')
  - [ReadAllLinesAsync()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllLinesAsync-System-String,System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllLinesAsync(System.String,System.Threading.CancellationToken)')
  - [ReadAllText()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllText-System-String- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllText(System.String)')
  - [ReadAllTextAsync()](#M-Obsidian-API-Plugins-Services-IFileReader-ReadAllTextAsync-System-String,System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileReader.ReadAllTextAsync(System.String,System.Threading.CancellationToken)')
- [IFileService](#T-Obsidian-API-Plugins-Services-IO-IFileService 'Obsidian.API.Plugins.Services.IO.IFileService')
  - [CombinePath()](#M-Obsidian-API-Plugins-Services-IO-IFileService-CombinePath-System-String[]- 'Obsidian.API.Plugins.Services.IO.IFileService.CombinePath(System.String[])')
  - [CreateWorkingDirectory(createOwnDirectory,skipFolderAutoGeneration)](#M-Obsidian-API-Plugins-Services-IO-IFileService-CreateWorkingDirectory-System-Boolean,System-Boolean- 'Obsidian.API.Plugins.Services.IO.IFileService.CreateWorkingDirectory(System.Boolean,System.Boolean)')
  - [DirectoryExists()](#M-Obsidian-API-Plugins-Services-IO-IFileService-DirectoryExists-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.DirectoryExists(System.String)')
  - [FileExists()](#M-Obsidian-API-Plugins-Services-IO-IFileService-FileExists-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.FileExists(System.String)')
  - [GetExtension()](#M-Obsidian-API-Plugins-Services-IO-IFileService-GetExtension-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.GetExtension(System.String)')
  - [GetFileName()](#M-Obsidian-API-Plugins-Services-IO-IFileService-GetFileName-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.GetFileName(System.String)')
  - [GetFileNameWithoutExtension()](#M-Obsidian-API-Plugins-Services-IO-IFileService-GetFileNameWithoutExtension-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.GetFileNameWithoutExtension(System.String)')
  - [GetFullPath()](#M-Obsidian-API-Plugins-Services-IO-IFileService-GetFullPath-System-String- 'Obsidian.API.Plugins.Services.IO.IFileService.GetFullPath(System.String)')
  - [GetWorkingDirectory()](#M-Obsidian-API-Plugins-Services-IO-IFileService-GetWorkingDirectory 'Obsidian.API.Plugins.Services.IO.IFileService.GetWorkingDirectory')
- [IFileWriter](#T-Obsidian-API-Plugins-Services-IFileWriter 'Obsidian.API.Plugins.Services.IFileWriter')
  - [AppendText()](#M-Obsidian-API-Plugins-Services-IFileWriter-AppendText-System-String,System-String- 'Obsidian.API.Plugins.Services.IFileWriter.AppendText(System.String,System.String)')
  - [AppendTextAsync()](#M-Obsidian-API-Plugins-Services-IFileWriter-AppendTextAsync-System-String,System-String,System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileWriter.AppendTextAsync(System.String,System.String,System.Threading.CancellationToken)')
  - [CopyFile()](#M-Obsidian-API-Plugins-Services-IFileWriter-CopyFile-System-String,System-String- 'Obsidian.API.Plugins.Services.IFileWriter.CopyFile(System.String,System.String)')
  - [CreateDirectory()](#M-Obsidian-API-Plugins-Services-IFileWriter-CreateDirectory-System-String- 'Obsidian.API.Plugins.Services.IFileWriter.CreateDirectory(System.String)')
  - [CreateFile()](#M-Obsidian-API-Plugins-Services-IFileWriter-CreateFile-System-String- 'Obsidian.API.Plugins.Services.IFileWriter.CreateFile(System.String)')
  - [DeleteDirectory()](#M-Obsidian-API-Plugins-Services-IFileWriter-DeleteDirectory-System-String- 'Obsidian.API.Plugins.Services.IFileWriter.DeleteDirectory(System.String)')
  - [DeleteFile()](#M-Obsidian-API-Plugins-Services-IFileWriter-DeleteFile-System-String- 'Obsidian.API.Plugins.Services.IFileWriter.DeleteFile(System.String)')
  - [MoveFile()](#M-Obsidian-API-Plugins-Services-IFileWriter-MoveFile-System-String,System-String- 'Obsidian.API.Plugins.Services.IFileWriter.MoveFile(System.String,System.String)')
  - [OpenWrite()](#M-Obsidian-API-Plugins-Services-IFileWriter-OpenWrite-System-String- 'Obsidian.API.Plugins.Services.IFileWriter.OpenWrite(System.String)')
  - [WriteAllBytes()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllBytes-System-String,System-Byte[]- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllBytes(System.String,System.Byte[])')
  - [WriteAllBytesAsync()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllBytesAsync-System-String,System-Byte[],System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllBytesAsync(System.String,System.Byte[],System.Threading.CancellationToken)')
  - [WriteAllLines()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllLines-System-String,System-String[]- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllLines(System.String,System.String[])')
  - [WriteAllLinesAsync()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllLinesAsync-System-String,System-String[],System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllLinesAsync(System.String,System.String[],System.Threading.CancellationToken)')
  - [WriteAllText()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllText-System-String,System-String- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllText(System.String,System.String)')
  - [WriteAllTextAsync()](#M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllTextAsync-System-String,System-String,System-Threading-CancellationToken- 'Obsidian.API.Plugins.Services.IFileWriter.WriteAllTextAsync(System.String,System.String,System.Threading.CancellationToken)')
- [ILogger](#T-Obsidian-API-Plugins-Services-ILogger 'Obsidian.API.Plugins.Services.ILogger')
  - [Log()](#M-Obsidian-API-Plugins-Services-ILogger-Log-System-String- 'Obsidian.API.Plugins.Services.ILogger.Log(System.String)')
  - [LogDebug(message)](#M-Obsidian-API-Plugins-Services-ILogger-LogDebug-System-String- 'Obsidian.API.Plugins.Services.ILogger.LogDebug(System.String)')
  - [LogError()](#M-Obsidian-API-Plugins-Services-ILogger-LogError-System-String- 'Obsidian.API.Plugins.Services.ILogger.LogError(System.String)')
  - [LogError\`\`1()](#M-Obsidian-API-Plugins-Services-ILogger-LogError``1-``0- 'Obsidian.API.Plugins.Services.ILogger.LogError``1(``0)')
  - [LogTrace()](#M-Obsidian-API-Plugins-Services-ILogger-LogTrace-System-String- 'Obsidian.API.Plugins.Services.ILogger.LogTrace(System.String)')
  - [LogTrace\`\`1()](#M-Obsidian-API-Plugins-Services-ILogger-LogTrace``1-``0- 'Obsidian.API.Plugins.Services.ILogger.LogTrace``1(``0)')
  - [LogWarning()](#M-Obsidian-API-Plugins-Services-ILogger-LogWarning-System-String- 'Obsidian.API.Plugins.Services.ILogger.LogWarning(System.String)')
- [INativeLoader](#T-Obsidian-API-Plugins-Services-INativeLoader 'Obsidian.API.Plugins.Services.INativeLoader')
  - [LoadMethod\`\`1()](#M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String- 'Obsidian.API.Plugins.Services.INativeLoader.LoadMethod``1(System.String)')
  - [LoadMethod\`\`1()](#M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-Text-Encoding- 'Obsidian.API.Plugins.Services.INativeLoader.LoadMethod``1(System.String,System.Text.Encoding)')
  - [LoadMethod\`\`1()](#M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-String- 'Obsidian.API.Plugins.Services.INativeLoader.LoadMethod``1(System.String,System.String)')
  - [LoadMethod\`\`1()](#M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-String,System-Text-Encoding- 'Obsidian.API.Plugins.Services.INativeLoader.LoadMethod``1(System.String,System.String,System.Text.Encoding)')
- [INetworkClient](#T-Obsidian-API-Plugins-Services-INetworkClient 'Obsidian.API.Plugins.Services.INetworkClient')
- [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess')
  - [ExitTime](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-ExitTime 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.ExitTime')
  - [Id](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Id 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.Id')
  - [Name](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Name 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.Name')
  - [NonpagedSystemMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-NonpagedSystemMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.NonpagedSystemMemorySize')
  - [PagedMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PagedMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PagedMemorySize')
  - [PagedSystemMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PagedSystemMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PagedSystemMemorySize')
  - [PeakPagedMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakPagedMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PeakPagedMemorySize')
  - [PeakVirtualMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakVirtualMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PeakVirtualMemorySize')
  - [PeakWorkingSet](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakWorkingSet 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PeakWorkingSet')
  - [PrivateMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PrivateMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.PrivateMemorySize')
  - [StartTime](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-StartTime 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.StartTime')
  - [ThreadCount](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-ThreadCount 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.ThreadCount')
  - [VirtualMemorySize](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-VirtualMemorySize 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.VirtualMemorySize')
  - [WorkingSet](#P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-WorkingSet 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.WorkingSet')
  - [Close()](#M-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Close 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.Close')
  - [Kill()](#M-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Kill 'Obsidian.API.Plugins.Services.Diagnostics.IProcess.Kill')
- [ISecuredService](#T-Obsidian-API-Plugins-Services-Common-ISecuredService 'Obsidian.API.Plugins.Services.Common.ISecuredService')
  - [IsUsable](#P-Obsidian-API-Plugins-Services-Common-ISecuredService-IsUsable 'Obsidian.API.Plugins.Services.Common.ISecuredService.IsUsable')
- [IServerStatus](#T-Obsidian-API-IServerStatus 'Obsidian.API.IServerStatus')
  - [Favicon](#P-Obsidian-API-IServerStatus-Favicon 'Obsidian.API.IServerStatus.Favicon')
- [IService](#T-Obsidian-API-Plugins-Services-Common-IService 'Obsidian.API.Plugins.Services.Common.IService')
- [IStopwatch](#T-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch')
  - [Elapsed](#P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Elapsed 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.Elapsed')
  - [ElapsedMilliseconds](#P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-ElapsedMilliseconds 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.ElapsedMilliseconds')
  - [ElapsedTicks](#P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-ElapsedTicks 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.ElapsedTicks')
  - [IsRunning](#P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-IsRunning 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.IsRunning')
  - [Reset()](#M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Reset 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.Reset')
  - [Restart()](#M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Restart 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.Restart')
  - [Start()](#M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Start 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.Start')
  - [Stop()](#M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Stop 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch.Stop')
- [IStream](#T-Obsidian-API-Plugins-Services-IO-IStream 'Obsidian.API.Plugins.Services.IO.IStream')
  - [CanRead](#P-Obsidian-API-Plugins-Services-IO-IStream-CanRead 'Obsidian.API.Plugins.Services.IO.IStream.CanRead')
  - [CanWrite](#P-Obsidian-API-Plugins-Services-IO-IStream-CanWrite 'Obsidian.API.Plugins.Services.IO.IStream.CanWrite')
  - [Name](#P-Obsidian-API-Plugins-Services-IO-IStream-Name 'Obsidian.API.Plugins.Services.IO.IStream.Name')
  - [Close()](#M-Obsidian-API-Plugins-Services-IO-IStream-Close 'Obsidian.API.Plugins.Services.IO.IStream.Close')
  - [CopyTo()](#M-Obsidian-API-Plugins-Services-IO-IStream-CopyTo-Obsidian-API-Plugins-Services-IO-IStream- 'Obsidian.API.Plugins.Services.IO.IStream.CopyTo(Obsidian.API.Plugins.Services.IO.IStream)')
  - [CopyToAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-CopyToAsync-Obsidian-API-Plugins-Services-IO-IStream- 'Obsidian.API.Plugins.Services.IO.IStream.CopyToAsync(Obsidian.API.Plugins.Services.IO.IStream)')
  - [Flush()](#M-Obsidian-API-Plugins-Services-IO-IStream-Flush 'Obsidian.API.Plugins.Services.IO.IStream.Flush')
  - [FlushAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-FlushAsync 'Obsidian.API.Plugins.Services.IO.IStream.FlushAsync')
  - [Read()](#M-Obsidian-API-Plugins-Services-IO-IStream-Read-System-Byte[],System-Int32,System-Int32- 'Obsidian.API.Plugins.Services.IO.IStream.Read(System.Byte[],System.Int32,System.Int32)')
  - [ReadAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadAsync-System-Byte[],System-Int32,System-Int32- 'Obsidian.API.Plugins.Services.IO.IStream.ReadAsync(System.Byte[],System.Int32,System.Int32)')
  - [ReadByte()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadByte 'Obsidian.API.Plugins.Services.IO.IStream.ReadByte')
  - [ReadLine()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadLine 'Obsidian.API.Plugins.Services.IO.IStream.ReadLine')
  - [ReadLineAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadLineAsync 'Obsidian.API.Plugins.Services.IO.IStream.ReadLineAsync')
  - [ReadToEnd()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadToEnd 'Obsidian.API.Plugins.Services.IO.IStream.ReadToEnd')
  - [ReadToEndAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-ReadToEndAsync 'Obsidian.API.Plugins.Services.IO.IStream.ReadToEndAsync')
  - [Write()](#M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-Byte[],System-Int32,System-Int32- 'Obsidian.API.Plugins.Services.IO.IStream.Write(System.Byte[],System.Int32,System.Int32)')
  - [Write()](#M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-String- 'Obsidian.API.Plugins.Services.IO.IStream.Write(System.String)')
  - [Write()](#M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-Object- 'Obsidian.API.Plugins.Services.IO.IStream.Write(System.Object)')
  - [WriteAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-Byte[],System-Int32,System-Int32- 'Obsidian.API.Plugins.Services.IO.IStream.WriteAsync(System.Byte[],System.Int32,System.Int32)')
  - [WriteAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-String- 'Obsidian.API.Plugins.Services.IO.IStream.WriteAsync(System.String)')
  - [WriteAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-Object- 'Obsidian.API.Plugins.Services.IO.IStream.WriteAsync(System.Object)')
  - [WriteByte()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteByte-System-Byte- 'Obsidian.API.Plugins.Services.IO.IStream.WriteByte(System.Byte)')
  - [WriteLine()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteLine-System-String- 'Obsidian.API.Plugins.Services.IO.IStream.WriteLine(System.String)')
  - [WriteLine()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteLine-System-Object- 'Obsidian.API.Plugins.Services.IO.IStream.WriteLine(System.Object)')
  - [WriteLineAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteLineAsync-System-String- 'Obsidian.API.Plugins.Services.IO.IStream.WriteLineAsync(System.String)')
  - [WriteLineAsync()](#M-Obsidian-API-Plugins-Services-IO-IStream-WriteLineAsync-System-Object- 'Obsidian.API.Plugins.Services.IO.IStream.WriteLineAsync(System.Object)')
- [IncomingChatMessageEventArgs](#T-Obsidian-API-Events-IncomingChatMessageEventArgs 'Obsidian.API.Events.IncomingChatMessageEventArgs')
  - [Message](#P-Obsidian-API-Events-IncomingChatMessageEventArgs-Message 'Obsidian.API.Events.IncomingChatMessageEventArgs.Message')
- [InjectAttribute](#T-Obsidian-API-Plugins-InjectAttribute 'Obsidian.API.Plugins.InjectAttribute')
- [InventoryClickEventArgs](#T-Obsidian-API-Events-InventoryClickEventArgs 'Obsidian.API.Events.InventoryClickEventArgs')
  - [Inventory](#P-Obsidian-API-Events-InventoryClickEventArgs-Inventory 'Obsidian.API.Events.InventoryClickEventArgs.Inventory')
  - [Item](#P-Obsidian-API-Events-InventoryClickEventArgs-Item 'Obsidian.API.Events.InventoryClickEventArgs.Item')
  - [Slot](#P-Obsidian-API-Events-InventoryClickEventArgs-Slot 'Obsidian.API.Events.InventoryClickEventArgs.Slot')
- [PlayerEventArgs](#T-Obsidian-API-Events-PlayerEventArgs 'Obsidian.API.Events.PlayerEventArgs')
  - [Player](#P-Obsidian-API-Events-PlayerEventArgs-Player 'Obsidian.API.Events.PlayerEventArgs.Player')
- [PlayerJoinEventArgs](#T-Obsidian-API-Events-PlayerJoinEventArgs 'Obsidian.API.Events.PlayerJoinEventArgs')
  - [JoinDate](#P-Obsidian-API-Events-PlayerJoinEventArgs-JoinDate 'Obsidian.API.Events.PlayerJoinEventArgs.JoinDate')
- [PluginAttribute](#T-Obsidian-API-Plugins-PluginAttribute 'Obsidian.API.Plugins.PluginAttribute')
  - [Authors](#P-Obsidian-API-Plugins-PluginAttribute-Authors 'Obsidian.API.Plugins.PluginAttribute.Authors')
  - [Description](#P-Obsidian-API-Plugins-PluginAttribute-Description 'Obsidian.API.Plugins.PluginAttribute.Description')
  - [Name](#P-Obsidian-API-Plugins-PluginAttribute-Name 'Obsidian.API.Plugins.PluginAttribute.Name')
  - [ProjectUrl](#P-Obsidian-API-Plugins-PluginAttribute-ProjectUrl 'Obsidian.API.Plugins.PluginAttribute.ProjectUrl')
  - [Version](#P-Obsidian-API-Plugins-PluginAttribute-Version 'Obsidian.API.Plugins.PluginAttribute.Version')
- [PluginBase](#T-Obsidian-API-Plugins-PluginBase 'Obsidian.API.Plugins.PluginBase')
  - [FriendlyInvokeAsync()](#M-Obsidian-API-Plugins-PluginBase-FriendlyInvokeAsync-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginBase.FriendlyInvokeAsync(System.String,System.Object[])')
  - [GetMethod\`\`1()](#M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginBase.GetMethod``1(System.String,System.Type[])')
  - [GetPropertyGetter\`\`1()](#M-Obsidian-API-Plugins-PluginBase-GetPropertyGetter``1-System-String- 'Obsidian.API.Plugins.PluginBase.GetPropertyGetter``1(System.String)')
  - [GetPropertySetter\`\`1()](#M-Obsidian-API-Plugins-PluginBase-GetPropertySetter``1-System-String- 'Obsidian.API.Plugins.PluginBase.GetPropertySetter``1(System.String)')
  - [Invoke()](#M-Obsidian-API-Plugins-PluginBase-Invoke-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginBase.Invoke(System.String,System.Object[])')
  - [InvokeAsync()](#M-Obsidian-API-Plugins-PluginBase-InvokeAsync-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginBase.InvokeAsync(System.String,System.Object[])')
  - [InvokeAsync\`\`1()](#M-Obsidian-API-Plugins-PluginBase-InvokeAsync``1-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginBase.InvokeAsync``1(System.String,System.Object[])')
  - [Invoke\`\`1()](#M-Obsidian-API-Plugins-PluginBase-Invoke``1-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginBase.Invoke``1(System.String,System.Object[])')
  - [Unload()](#M-Obsidian-API-Plugins-PluginBase-Unload 'Obsidian.API.Plugins.PluginBase.Unload')
- [PluginPermissions](#T-Obsidian-API-Plugins-PluginPermissions 'Obsidian.API.Plugins.PluginPermissions')
  - [CanRead](#F-Obsidian-API-Plugins-PluginPermissions-CanRead 'Obsidian.API.Plugins.PluginPermissions.CanRead')
  - [CanWrite](#F-Obsidian-API-Plugins-PluginPermissions-CanWrite 'Obsidian.API.Plugins.PluginPermissions.CanWrite')
  - [Compilation](#F-Obsidian-API-Plugins-PluginPermissions-Compilation 'Obsidian.API.Plugins.PluginPermissions.Compilation')
  - [FileAccess](#F-Obsidian-API-Plugins-PluginPermissions-FileAccess 'Obsidian.API.Plugins.PluginPermissions.FileAccess')
  - [Interop](#F-Obsidian-API-Plugins-PluginPermissions-Interop 'Obsidian.API.Plugins.PluginPermissions.Interop')
  - [NetworkAccess](#F-Obsidian-API-Plugins-PluginPermissions-NetworkAccess 'Obsidian.API.Plugins.PluginPermissions.NetworkAccess')
  - [Reflection](#F-Obsidian-API-Plugins-PluginPermissions-Reflection 'Obsidian.API.Plugins.PluginPermissions.Reflection')
  - [RunningSubprocesses](#F-Obsidian-API-Plugins-PluginPermissions-RunningSubprocesses 'Obsidian.API.Plugins.PluginPermissions.RunningSubprocesses')
  - [ThirdPartyLibraries](#F-Obsidian-API-Plugins-PluginPermissions-ThirdPartyLibraries 'Obsidian.API.Plugins.PluginPermissions.ThirdPartyLibraries')
- [PluginWrapper](#T-Obsidian-API-Plugins-PluginWrapper 'Obsidian.API.Plugins.PluginWrapper')
  - [GetMethod\`\`1()](#M-Obsidian-API-Plugins-PluginWrapper-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginWrapper.GetMethod``1(System.String,System.Type[])')
  - [GetPropertyGetter\`\`1()](#M-Obsidian-API-Plugins-PluginWrapper-GetPropertyGetter``1-System-String- 'Obsidian.API.Plugins.PluginWrapper.GetPropertyGetter``1(System.String)')
  - [GetPropertySetter\`\`1()](#M-Obsidian-API-Plugins-PluginWrapper-GetPropertySetter``1-System-String- 'Obsidian.API.Plugins.PluginWrapper.GetPropertySetter``1(System.String)')
  - [Invoke()](#M-Obsidian-API-Plugins-PluginWrapper-Invoke-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginWrapper.Invoke(System.String,System.Object[])')
  - [Invoke\`\`1()](#M-Obsidian-API-Plugins-PluginWrapper-Invoke``1-System-String,System-Object[]- 'Obsidian.API.Plugins.PluginWrapper.Invoke``1(System.String,System.Object[])')
- [Position](#T-Obsidian-API-Position 'Obsidian.API.Position')
  - [#ctor(value)](#M-Obsidian-API-Position-#ctor-System-Int32- 'Obsidian.API.Position.#ctor(System.Int32)')
  - [#ctor(x,y,z)](#M-Obsidian-API-Position-#ctor-System-Int32,System-Int32,System-Int32- 'Obsidian.API.Position.#ctor(System.Int32,System.Int32,System.Int32)')
  - [Magnitude](#P-Obsidian-API-Position-Magnitude 'Obsidian.API.Position.Magnitude')
  - [X](#P-Obsidian-API-Position-X 'Obsidian.API.Position.X')
  - [Y](#P-Obsidian-API-Position-Y 'Obsidian.API.Position.Y')
  - [Z](#P-Obsidian-API-Position-Z 'Obsidian.API.Position.Z')
  - [ChunkClamp()](#M-Obsidian-API-Position-ChunkClamp 'Obsidian.API.Position.ChunkClamp')
  - [Clamp()](#M-Obsidian-API-Position-Clamp-Obsidian-API-Position,Obsidian-API-Position- 'Obsidian.API.Position.Clamp(Obsidian.API.Position,Obsidian.API.Position)')
  - [DistanceTo()](#M-Obsidian-API-Position-DistanceTo-Obsidian-API-Position,Obsidian-API-Position- 'Obsidian.API.Position.DistanceTo(Obsidian.API.Position,Obsidian.API.Position)')
  - [Equals()](#M-Obsidian-API-Position-Equals-Obsidian-API-Position- 'Obsidian.API.Position.Equals(Obsidian.API.Position)')
  - [Square()](#M-Obsidian-API-Position-Square-System-Int32- 'Obsidian.API.Position.Square(System.Int32)')
  - [ToString()](#M-Obsidian-API-Position-ToString 'Obsidian.API.Position.ToString')
- [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF')
  - [#ctor(value)](#M-Obsidian-API-PositionF-#ctor-System-Single- 'Obsidian.API.PositionF.#ctor(System.Single)')
  - [#ctor(x,y,z)](#M-Obsidian-API-PositionF-#ctor-System-Int32,System-Int32,System-Int32- 'Obsidian.API.PositionF.#ctor(System.Int32,System.Int32,System.Int32)')
  - [#ctor(x,y,z)](#M-Obsidian-API-PositionF-#ctor-System-Single,System-Single,System-Single- 'Obsidian.API.PositionF.#ctor(System.Single,System.Single,System.Single)')
  - [Magnitude](#P-Obsidian-API-PositionF-Magnitude 'Obsidian.API.PositionF.Magnitude')
  - [X](#P-Obsidian-API-PositionF-X 'Obsidian.API.PositionF.X')
  - [Y](#P-Obsidian-API-PositionF-Y 'Obsidian.API.PositionF.Y')
  - [Z](#P-Obsidian-API-PositionF-Z 'Obsidian.API.PositionF.Z')
  - [ChunkClamp()](#M-Obsidian-API-PositionF-ChunkClamp 'Obsidian.API.PositionF.ChunkClamp')
  - [Clamp()](#M-Obsidian-API-PositionF-Clamp-Obsidian-API-PositionF,Obsidian-API-PositionF- 'Obsidian.API.PositionF.Clamp(Obsidian.API.PositionF,Obsidian.API.PositionF)')
  - [DistanceTo()](#M-Obsidian-API-PositionF-DistanceTo-Obsidian-API-PositionF,Obsidian-API-PositionF- 'Obsidian.API.PositionF.DistanceTo(Obsidian.API.PositionF,Obsidian.API.PositionF)')
  - [Equals()](#M-Obsidian-API-PositionF-Equals-Obsidian-API-PositionF- 'Obsidian.API.PositionF.Equals(Obsidian.API.PositionF)')
  - [Floor()](#M-Obsidian-API-PositionF-Floor 'Obsidian.API.PositionF.Floor')
  - [Normalize()](#M-Obsidian-API-PositionF-Normalize 'Obsidian.API.PositionF.Normalize')
  - [Square()](#M-Obsidian-API-PositionF-Square-System-Single- 'Obsidian.API.PositionF.Square(System.Single)')
  - [ToString()](#M-Obsidian-API-PositionF-ToString 'Obsidian.API.PositionF.ToString')
- [SoundCategory](#T-Obsidian-API-SoundCategory 'Obsidian.API.SoundCategory')
- [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity')
  - [#ctor(x,y,z)](#M-Obsidian-API-Velocity-#ctor-System-Int16,System-Int16,System-Int16- 'Obsidian.API.Velocity.#ctor(System.Int16,System.Int16,System.Int16)')
  - [Magnitude](#P-Obsidian-API-Velocity-Magnitude 'Obsidian.API.Velocity.Magnitude')
  - [X](#P-Obsidian-API-Velocity-X 'Obsidian.API.Velocity.X')
  - [Y](#P-Obsidian-API-Velocity-Y 'Obsidian.API.Velocity.Y')
  - [Z](#P-Obsidian-API-Velocity-Z 'Obsidian.API.Velocity.Z')
  - [FromBlockPerSecond(x,y,z)](#M-Obsidian-API-Velocity-FromBlockPerSecond-System-Single,System-Single,System-Single- 'Obsidian.API.Velocity.FromBlockPerSecond(System.Single,System.Single,System.Single)')
  - [FromBlockPerTick(x,y,z)](#M-Obsidian-API-Velocity-FromBlockPerTick-System-Single,System-Single,System-Single- 'Obsidian.API.Velocity.FromBlockPerTick(System.Single,System.Single,System.Single)')
  - [FromDirection(from,to)](#M-Obsidian-API-Velocity-FromDirection-Obsidian-API-Position,Obsidian-API-Position- 'Obsidian.API.Velocity.FromDirection(Obsidian.API.Position,Obsidian.API.Position)')
  - [FromDirection(from,to)](#M-Obsidian-API-Velocity-FromDirection-Obsidian-API-PositionF,Obsidian-API-PositionF- 'Obsidian.API.Velocity.FromDirection(Obsidian.API.PositionF,Obsidian.API.PositionF)')
  - [FromPosition(position)](#M-Obsidian-API-Velocity-FromPosition-Obsidian-API-Position- 'Obsidian.API.Velocity.FromPosition(Obsidian.API.Position)')
  - [FromPosition(position)](#M-Obsidian-API-Velocity-FromPosition-Obsidian-API-PositionF- 'Obsidian.API.Velocity.FromPosition(Obsidian.API.PositionF)')

<a name='T-Obsidian-API-Plugins-AliasAttribute'></a>
## AliasAttribute `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Specifies the property/field name that is used for dependency injection.

<a name='M-Obsidian-API-Plugins-AliasAttribute-#ctor-System-String-'></a>
### #ctor(identifier) `constructor`

##### Summary

Initializes a new instance of [AliasAttribute](#T-Obsidian-API-Plugins-AliasAttribute 'Obsidian.API.Plugins.AliasAttribute') with the specified identifier.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| identifier | [System.String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String') | Name that is used for dependency injection. |

<a name='P-Obsidian-API-Plugins-AliasAttribute-Identifier'></a>
### Identifier `property`

##### Summary

Name that is used for dependency injection.

<a name='T-Obsidian-API-Angle'></a>
## Angle `type`

##### Namespace

Obsidian.API

##### Summary

A class that represents an angle from 0° to 360° degrees.

<a name='T-Obsidian-API-Events-AsyncEvent'></a>
## AsyncEvent `type`

##### Namespace

Obsidian.API.Events

##### Summary

Represents an asynchronously-handled event.

<a name='T-Obsidian-API-Events-AsyncEventArgs'></a>
## AsyncEventArgs `type`

##### Namespace

Obsidian.API.Events

##### Summary

Represents asynchronous event arguments.

<a name='P-Obsidian-API-Events-AsyncEventArgs-Handled'></a>
### Handled `property`

##### Summary

Gets or sets whether the event was completely handled. Setting this to true will prevent remaining handlers from running.

<a name='T-Obsidian-API-Events-AsyncEventHandler'></a>
## AsyncEventHandler `type`

##### Namespace

Obsidian.API.Events

##### Summary

Represents an asynchronous event handler.

##### Returns

Event handling task.

<a name='T-Obsidian-API-Events-AsyncEventHandler`1'></a>
## AsyncEventHandler\`1 `type`

##### Namespace

Obsidian.API.Events

##### Summary

Represents an asynchronous event handler.

##### Returns

Event handling task.

##### Generic Types

| Name | Description |
| ---- | ----------- |
| T | Type of EventArgs for the event. |

<a name='T-Obsidian-API-Events-AsyncEvent`1'></a>
## AsyncEvent\`1 `type`

##### Namespace

Obsidian.API.Events

##### Summary

Represents an asynchronously-handled event.

##### Generic Types

| Name | Description |
| ---- | ----------- |
| T | Type of EventArgs for this event. |

<a name='T-Obsidian-API-Events-BaseMinecraftEventArgs'></a>
## BaseMinecraftEventArgs `type`

##### Namespace

Obsidian.API.Events

<a name='P-Obsidian-API-Events-BaseMinecraftEventArgs-Server'></a>
### Server `property`

##### Summary

Server this event took place in.

<a name='T-Obsidian-API-BlockFace'></a>
## BlockFace `type`

##### Namespace

Obsidian.API

<a name='F-Obsidian-API-BlockFace-Bottom'></a>
### Bottom `constants`

##### Summary

-Y

<a name='F-Obsidian-API-BlockFace-East'></a>
### East `constants`

##### Summary

+X

<a name='F-Obsidian-API-BlockFace-North'></a>
### North `constants`

##### Summary

-Z

<a name='F-Obsidian-API-BlockFace-South'></a>
### South `constants`

##### Summary

+Z

<a name='F-Obsidian-API-BlockFace-Top'></a>
### Top `constants`

##### Summary

+Y

<a name='F-Obsidian-API-BlockFace-West'></a>
### West `constants`

##### Summary

-X

<a name='T-Obsidian-CommandFramework-Entities-Command'></a>
## Command `type`

##### Namespace

Obsidian.CommandFramework.Entities

<a name='M-Obsidian-CommandFramework-Entities-Command-ExecuteAsync-Obsidian-CommandFramework-ObsidianContext,System-String[]-'></a>
### ExecuteAsync(Context) `method`

##### Summary

Executes this command.

##### Returns



##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| Context | [Obsidian.CommandFramework.ObsidianContext](#T-Obsidian-CommandFramework-ObsidianContext 'Obsidian.CommandFramework.ObsidianContext') | Execution context. |

##### Generic Types

| Name | Description |
| ---- | ----------- |
| T | Context type. |

<a name='M-Obsidian-CommandFramework-Entities-Command-GetQualifiedName'></a>
### GetQualifiedName() `method`

##### Summary

Gets the full qualified command name.

##### Returns

Full qualified command name.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-DependencyAttribute'></a>
## DependencyAttribute `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Indicates that the field/property should have it's value injected with a plugin.

<a name='P-Obsidian-API-Plugins-DependencyAttribute-MinVersion'></a>
### MinVersion `property`

##### Summary

Minimal version of the dependency that can be used. The string should contain the major, minor, , and numbers, split by a period character ('.').

<a name='P-Obsidian-API-Plugins-DependencyAttribute-Optional'></a>
### Optional `property`

##### Summary

Indicates whether the plugin can run without this dependency.

<a name='M-Obsidian-API-Plugins-DependencyAttribute-GetMinVersion'></a>
### GetMinVersion() `method`

##### Summary

Gets [MinVersion](#P-Obsidian-API-Plugins-DependencyAttribute-MinVersion 'Obsidian.API.Plugins.DependencyAttribute.MinVersion') parsed to [Version](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Version 'System.Version') if possible, otherwise returns `new Version()`.

##### Returns

[MinVersion](#P-Obsidian-API-Plugins-DependencyAttribute-MinVersion 'Obsidian.API.Plugins.DependencyAttribute.MinVersion') parsed to [Version](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Version 'System.Version') if possible, otherwise returns `new Version()`.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-EClickAction'></a>
## EClickAction `type`

##### Namespace

Obsidian.API

<a name='F-Obsidian-API-EClickAction-ChangePage'></a>
### ChangePage `constants`

##### Summary

Only usable within written books. Changes the page of the book to the given page, starting at 1. For instance, "value":1 switches the book to the first page.
If the page is less than one or beyond the number of pages in the book, the event is ignored.

<a name='F-Obsidian-API-EClickAction-CopyToClipboard'></a>
### CopyToClipboard `constants`

##### Summary

Copies Value to the clipboard.

<a name='F-Obsidian-API-EClickAction-OpenUrl'></a>
### OpenUrl `constants`

##### Summary

Opens the given URL in the default web browser. Ignored if the player has opted to disable links in chat;
may open a GUI prompting the user if the setting for that is enabled. The link's protocol must be set and must be http or https, for security reasons.

<a name='F-Obsidian-API-EClickAction-RunCommand'></a>
### RunCommand `constants`

##### Summary

Runs the given command. Not required to be a command - clicking this only causes the client to send the given content as a chat message, so if not prefixed with /, they will say the given text instead.
If used in a book GUI, the GUI is closed after clicking

<a name='F-Obsidian-API-EClickAction-SuggestCommand'></a>
### SuggestCommand `constants`

##### Summary

Only usable for messages in chat. Replaces the content of the chat box with the given text - usually a command, but it is not required to be a command (commands should be prefixed with /).

<a name='T-Obsidian-API-EHoverAction'></a>
## EHoverAction `type`

##### Namespace

Obsidian.API

<a name='F-Obsidian-API-EHoverAction-ShowEntity'></a>
### ShowEntity `constants`

##### Summary

Shows an entity's name, type, and UUID.

<a name='F-Obsidian-API-EHoverAction-ShowItem'></a>
### ShowItem `constants`

##### Summary

Shows the tooltip of an item as if it was being hovering over it in an inventory.

<a name='F-Obsidian-API-EHoverAction-ShowText'></a>
### ShowText `constants`

##### Summary

Shows a raw JSON text component.

<a name='T-Obsidian-API-IConfig'></a>
## IConfig `type`

##### Namespace

Obsidian.API

<a name='P-Obsidian-API-IConfig-DownloadPlugins'></a>
### DownloadPlugins `property`

##### Summary

Paths of plugins that are loaded at the starttime.

<a name='P-Obsidian-API-IConfig-Footer'></a>
### Footer `property`

##### Summary

Lower text in the in-game TAB menu.

<a name='P-Obsidian-API-IConfig-Generator'></a>
### Generator `property`

##### Summary

Name of the world generator to be used.

<a name='P-Obsidian-API-IConfig-Header'></a>
### Header `property`

##### Summary

Upper text in the in-game TAB menu.

<a name='P-Obsidian-API-IConfig-JoinMessage'></a>
### JoinMessage `property`

##### Summary

Message, that is sent to the chat when player successfully joins the server.

<a name='P-Obsidian-API-IConfig-LeaveMessage'></a>
### LeaveMessage `property`

##### Summary

Message, that is sent to the chat when player leaves the server.

<a name='P-Obsidian-API-IConfig-MaxMissedKeepAlives'></a>
### MaxMissedKeepAlives `property`

##### Summary

How many KeepAlive packets can be ignored by the client before disconnecting.

<a name='P-Obsidian-API-IConfig-MaxPlayers'></a>
### MaxPlayers `property`

##### Summary

Maximum amount of players that is allowed to be connected at the same time.

<a name='P-Obsidian-API-IConfig-Motd'></a>
### Motd `property`

##### Summary

Server description.

<a name='P-Obsidian-API-IConfig-MulitplayerDebugMode'></a>
### MulitplayerDebugMode `property`

##### Summary

Whether each login/client gets a random username where multiple connections from the same host will be allowed.

<a name='P-Obsidian-API-IConfig-OnlineMode'></a>
### OnlineMode `property`

##### Summary

Whether the server uses MojangAPI for loading skins etc.

<a name='P-Obsidian-API-IConfig-Port'></a>
### Port `property`

##### Summary

The port on which to listen for incoming connection attempts.

<a name='P-Obsidian-API-IConfig-Seed'></a>
### Seed `property`

##### Summary

Seed supplied to the world generator.

<a name='T-Obsidian-API-Plugins-Services-IDiagnoser'></a>
## IDiagnoser `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used for process diagnoses.

<a name='M-Obsidian-API-Plugins-Services-IDiagnoser-GetProcess'></a>
### GetProcess() `method`

##### Summary

Gets a new [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') component and associates it with the currently active process.

##### Returns

A new [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') component associated with the process resource that is running the calling application.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IDiagnoser-GetProcesses'></a>
### GetProcesses() `method`

##### Summary

Creates a new [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') component for each process resource on the local computer.

##### Returns

An array of type [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') that represents all the process resources running on the local computer.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IDiagnoser-GetStopwatch'></a>
### GetStopwatch() `method`

##### Summary

Returns a new instance of [IStopwatch](#T-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch').

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IDiagnoser-StartProcess-System-String,System-String,System-Boolean,System-Boolean-'></a>
### StartProcess(fileName,arguments,createWindow,useShell) `method`

##### Summary

Starts a process resource by specifying the name of an application and a set of command-line arguments, and associates the resource with a new [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') component.

##### Returns

A new [IProcess](#T-Obsidian-API-Plugins-Services-Diagnostics-IProcess 'Obsidian.API.Plugins.Services.Diagnostics.IProcess') that is associated with the process resource, or null if no process resource is started.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| fileName | [System.String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String') | The application or document to start. |
| arguments | [System.String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String') | The set of command-line arguments to use when starting the application. |
| createWindow | [System.Boolean](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Boolean 'System.Boolean') | Indicates whether to start the process in a new window. |
| useShell | [System.Boolean](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Boolean 'System.Boolean') | Indicates whether to use the operating system shell to start the process. |

<a name='M-Obsidian-API-Plugins-Services-IDiagnoser-StartStopwatch'></a>
### StartStopwatch() `method`

##### Summary

Returns a new instance of [IStopwatch](#T-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch') and starts it.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-Services-IFileReader'></a>
## IFileReader `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used for reading from files.s

<a name='M-Obsidian-API-Plugins-Services-IFileReader-GetDirectoryFiles-System-String-'></a>
### GetDirectoryFiles() `method`

##### Summary

Returns the names of files (including their paths) in the specified directory.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-GetSubdirectories-System-String-'></a>
### GetSubdirectories() `method`

##### Summary

Returns the names of subdirectories (including their paths) in the specified directory.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-OpenRead-System-String-'></a>
### OpenRead() `method`

##### Summary

Opens an existing file for reading.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllBytes-System-String-'></a>
### ReadAllBytes() `method`

##### Summary

Opens a binary file, reads the contents of the file into a byte array, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllBytesAsync-System-String,System-Threading-CancellationToken-'></a>
### ReadAllBytesAsync() `method`

##### Summary

Asynchronously opens a binary file, reads the contents of the file into a byte array, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllLines-System-String-'></a>
### ReadAllLines() `method`

##### Summary

Opens a text file, reads all lines of the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllLinesAsync-System-String,System-Threading-CancellationToken-'></a>
### ReadAllLinesAsync() `method`

##### Summary

Asynchronously opens a text file, reads all lines of the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllText-System-String-'></a>
### ReadAllText() `method`

##### Summary

Opens a text file, reads all the text in the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileReader-ReadAllTextAsync-System-String,System-Threading-CancellationToken-'></a>
### ReadAllTextAsync() `method`

##### Summary

Asynchronously opens a text file, reads all the text in the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='T-Obsidian-API-Plugins-Services-IO-IFileService'></a>
## IFileService `type`

##### Namespace

Obsidian.API.Plugins.Services.IO

##### Summary

Provides the base interface for file services.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-CombinePath-System-String[]-'></a>
### CombinePath() `method`

##### Summary

Combines an array of strings into a path.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-CreateWorkingDirectory-System-Boolean,System-Boolean-'></a>
### CreateWorkingDirectory(createOwnDirectory,skipFolderAutoGeneration) `method`

##### Summary

Creates a directory, that is used by default for relative paths.

##### Returns

Path to the created directory.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| createOwnDirectory | [System.Boolean](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Boolean 'System.Boolean') | If set to , the automatically assigned directory for your plugin will be skipped. |
| skipFolderAutoGeneration | [System.Boolean](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Boolean 'System.Boolean') | If set to , skips the auto generation method for default plugin dir. Also, needs to be for this to work. |

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-DirectoryExists-System-String-'></a>
### DirectoryExists() `method`

##### Summary

Determines whether the given path refers to an existing directory on disk.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-FileExists-System-String-'></a>
### FileExists() `method`

##### Summary

Determines whether the specified file exists.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-GetExtension-System-String-'></a>
### GetExtension() `method`

##### Summary

Returns the extension (including the period ".") of the specified path string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-GetFileName-System-String-'></a>
### GetFileName() `method`

##### Summary

Returns the file name and extension of the specified path string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-GetFileNameWithoutExtension-System-String-'></a>
### GetFileNameWithoutExtension() `method`

##### Summary

Returns the file name of the specified path string without the extension.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-GetFullPath-System-String-'></a>
### GetFullPath() `method`

##### Summary

Returns the absolute path for the specified path string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IFileService-GetWorkingDirectory'></a>
### GetWorkingDirectory() `method`

##### Summary

Returns with the working directory.

##### Returns

Path to the created directory.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-Services-IFileWriter'></a>
## IFileWriter `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used for creating and writing to files.

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-AppendText-System-String,System-String-'></a>
### AppendText() `method`

##### Summary

Opens a file, appends the specified string to the file, and then closes the file. If the file does not exist, this method creates a file, writes the specified string to the file, then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-AppendTextAsync-System-String,System-String,System-Threading-CancellationToken-'></a>
### AppendTextAsync() `method`

##### Summary

Asynchronously opens a file or creates a file if it does not already exist, appends the specified string to the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-CopyFile-System-String,System-String-'></a>
### CopyFile() `method`

##### Summary

Copies an existing file to a new file. Overwriting a file of the same name is allowed.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-CreateDirectory-System-String-'></a>
### CreateDirectory() `method`

##### Summary

Creates all directories and subdirectories in the specified path unless they already exist.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-CreateFile-System-String-'></a>
### CreateFile() `method`

##### Summary

Creates or overwrites a file in the specified path.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-DeleteDirectory-System-String-'></a>
### DeleteDirectory() `method`

##### Summary

Deletes the specified directory and, if indicated, any subdirectories and files in the directory.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-DeleteFile-System-String-'></a>
### DeleteFile() `method`

##### Summary

Deletes the specified file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-MoveFile-System-String,System-String-'></a>
### MoveFile() `method`

##### Summary

Moves a specified file to a new location, providing the option to specify a new file name.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-OpenWrite-System-String-'></a>
### OpenWrite() `method`

##### Summary

Opens an existing file or creates a new file for writing.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllBytes-System-String,System-Byte[]-'></a>
### WriteAllBytes() `method`

##### Summary

Creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllBytesAsync-System-String,System-Byte[],System-Threading-CancellationToken-'></a>
### WriteAllBytesAsync() `method`

##### Summary

Asynchronously creates a new file, writes the specified byte array to the file, and then closes the file. If the target file already exists, it is overwritten.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllLines-System-String,System-String[]-'></a>
### WriteAllLines() `method`

##### Summary

Creates a new file, write the specified string array to the file, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllLinesAsync-System-String,System-String[],System-Threading-CancellationToken-'></a>
### WriteAllLinesAsync() `method`

##### Summary

Creates a new file, writes the specified string array to the file by using the specified encoding, and then closes the file.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllText-System-String,System-String-'></a>
### WriteAllText() `method`

##### Summary

Creates a new file, writes the specified string to the file, and then closes the file. If the target file already exists, it is overwritten.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-IFileWriter-WriteAllTextAsync-System-String,System-String,System-Threading-CancellationToken-'></a>
### WriteAllTextAsync() `method`

##### Summary

Asynchronously creates a new file, writes the specified string to the file, and then closes the file. If the target file already exists, it is overwritten.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='T-Obsidian-API-Plugins-Services-ILogger'></a>
## ILogger `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used to perform logging.

<a name='M-Obsidian-API-Plugins-Services-ILogger-Log-System-String-'></a>
### Log() `method`

##### Summary

Formats and writes an informational log message.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogDebug-System-String-'></a>
### LogDebug(message) `method`

##### Summary

Formats and writes a debug log message.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| message | [System.String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String') |  |

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogError-System-String-'></a>
### LogError() `method`

##### Summary

Formats and writes an error log message.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogError``1-``0-'></a>
### LogError\`\`1() `method`

##### Summary

Formats and writes an error log message.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogTrace-System-String-'></a>
### LogTrace() `method`

##### Summary

Formats and writes a trace log message.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogTrace``1-``0-'></a>
### LogTrace\`\`1() `method`

##### Summary

Formats and writes a trace log message.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-ILogger-LogWarning-System-String-'></a>
### LogWarning() `method`

##### Summary

Formats and writes a warning log message.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-Services-INativeLoader'></a>
## INativeLoader `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used for loading and using native libraries.

<a name='M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String-'></a>
### LoadMethod\`\`1() `method`

##### Summary

Attempts to load exported function from native library.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-Text-Encoding-'></a>
### LoadMethod\`\`1() `method`

##### Summary

Attempts to load exported function with specific string encoding from native library.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-String-'></a>
### LoadMethod\`\`1() `method`

##### Summary

Attempts to load exported function from native library.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-INativeLoader-LoadMethod``1-System-String,System-String,System-Text-Encoding-'></a>
### LoadMethod\`\`1() `method`

##### Summary

Attempts to load exported function with specific encoding from native library.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='T-Obsidian-API-Plugins-Services-INetworkClient'></a>
## INetworkClient `type`

##### Namespace

Obsidian.API.Plugins.Services

##### Summary

Represents a service used for performing actions over network.

<a name='T-Obsidian-API-Plugins-Services-Diagnostics-IProcess'></a>
## IProcess `type`

##### Namespace

Obsidian.API.Plugins.Services.Diagnostics

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-ExitTime'></a>
### ExitTime `property`

##### Summary

Gets the time that the associated process exited.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Id'></a>
### Id `property`

##### Summary

Gets the unique identifier for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Name'></a>
### Name `property`

##### Summary

Gets the name of the process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-NonpagedSystemMemorySize'></a>
### NonpagedSystemMemorySize `property`

##### Summary

Gets the amount of nonpaged system memory, in bytes, allocated for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PagedMemorySize'></a>
### PagedMemorySize `property`

##### Summary

Gets the amount of paged memory, in bytes, allocated for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PagedSystemMemorySize'></a>
### PagedSystemMemorySize `property`

##### Summary

Gets the amount of pageable system memory, in bytes, allocated for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakPagedMemorySize'></a>
### PeakPagedMemorySize `property`

##### Summary

Gets the maximum amount of memory in the virtual memory paging file, in bytes, used by the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakVirtualMemorySize'></a>
### PeakVirtualMemorySize `property`

##### Summary

Gets the maximum amount of virtual memory, in bytes, used by the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PeakWorkingSet'></a>
### PeakWorkingSet `property`

##### Summary

Gets the maximum amount of physical memory, in bytes, used by the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-PrivateMemorySize'></a>
### PrivateMemorySize `property`

##### Summary

Gets the amount of private memory, in bytes, allocated for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-StartTime'></a>
### StartTime `property`

##### Summary

Gets the time that the associated process was started.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-ThreadCount'></a>
### ThreadCount `property`

##### Summary

Gets the number of threads that are running in the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-VirtualMemorySize'></a>
### VirtualMemorySize `property`

##### Summary

Gets the amount of the virtual memory, in bytes, allocated for the associated process.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IProcess-WorkingSet'></a>
### WorkingSet `property`

##### Summary

Gets the amount of physical memory, in bytes, allocated for the associated process.

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Close'></a>
### Close() `method`

##### Summary

Frees all the resources that are associated with this component.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IProcess-Kill'></a>
### Kill() `method`

##### Summary

Immediately stops the associated process.

##### Parameters

This method has no parameters.

##### Exceptions

| Name | Description |
| ---- | ----------- |
| [System.Security.SecurityException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Security.SecurityException 'System.Security.SecurityException') |  |

<a name='T-Obsidian-API-Plugins-Services-Common-ISecuredService'></a>
## ISecuredService `type`

##### Namespace

Obsidian.API.Plugins.Services.Common

##### Summary

Provides the base interface for services that need permission to be used.

<a name='P-Obsidian-API-Plugins-Services-Common-ISecuredService-IsUsable'></a>
### IsUsable `property`

##### Summary

Gets a value indicating whether the [ISecuredService](#T-Obsidian-API-Plugins-Services-Common-ISecuredService 'Obsidian.API.Plugins.Services.Common.ISecuredService') has a permission to be used.

<a name='T-Obsidian-API-IServerStatus'></a>
## IServerStatus `type`

##### Namespace

Obsidian.API

<a name='P-Obsidian-API-IServerStatus-Favicon'></a>
### Favicon `property`

##### Summary

This is a base64 png image, that has dimensions of 64x64

<a name='T-Obsidian-API-Plugins-Services-Common-IService'></a>
## IService `type`

##### Namespace

Obsidian.API.Plugins.Services.Common

##### Summary

Provides the base interface for services.

<a name='T-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch'></a>
## IStopwatch `type`

##### Namespace

Obsidian.API.Plugins.Services.Diagnostics

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Elapsed'></a>
### Elapsed `property`

##### Summary

Gets the total elapsed time measured by the current instance.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-ElapsedMilliseconds'></a>
### ElapsedMilliseconds `property`

##### Summary

Gets the total elapsed time measured by the current instance, in milliseconds.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-ElapsedTicks'></a>
### ElapsedTicks `property`

##### Summary

Gets the total elapsed time measured by the current instance, in timer ticks.

<a name='P-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-IsRunning'></a>
### IsRunning `property`

##### Summary

Gets a value indicating whether the [IStopwatch](#T-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch 'Obsidian.API.Plugins.Services.Diagnostics.IStopwatch') timer is running.

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Reset'></a>
### Reset() `method`

##### Summary

Stops time interval measurement and resets the elapsed time to zero.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Restart'></a>
### Restart() `method`

##### Summary

Stops time interval measurement, resets the elapsed time to zero, and starts measuring elapsed time.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Start'></a>
### Start() `method`

##### Summary

Starts, or resumes, measuring elapsed time for an interval.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-Diagnostics-IStopwatch-Stop'></a>
### Stop() `method`

##### Summary

Stops measuring elapsed time for an interval.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-Services-IO-IStream'></a>
## IStream `type`

##### Namespace

Obsidian.API.Plugins.Services.IO

##### Summary

Provides a generic view of a sequence of bytes.

<a name='P-Obsidian-API-Plugins-Services-IO-IStream-CanRead'></a>
### CanRead `property`

##### Summary

Gets a value that indicates whether the current stream supports reading.

<a name='P-Obsidian-API-Plugins-Services-IO-IStream-CanWrite'></a>
### CanWrite `property`

##### Summary

Gets a value that indicates whether the current stream supports writing.

<a name='P-Obsidian-API-Plugins-Services-IO-IStream-Name'></a>
### Name `property`

##### Summary

Gets the absolute path of the data opened in the [IStream](#T-Obsidian-API-Plugins-Services-IO-IStream 'Obsidian.API.Plugins.Services.IO.IStream').

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Close'></a>
### Close() `method`

##### Summary

Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream.
Instead of calling this method, ensure that the stream is properly disposed.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-CopyTo-Obsidian-API-Plugins-Services-IO-IStream-'></a>
### CopyTo() `method`

##### Summary

Reads the bytes from the current stream and writes them to another stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-CopyToAsync-Obsidian-API-Plugins-Services-IO-IStream-'></a>
### CopyToAsync() `method`

##### Summary

Asynchronously reads the bytes from the current stream and writes them to another stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Flush'></a>
### Flush() `method`

##### Summary

Clears buffers for this stream and causes any buffered data to be written.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-FlushAsync'></a>
### FlushAsync() `method`

##### Summary

Asynchronously clears all buffers for this stream and causes any buffered data to be written to the underlying device.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Read-System-Byte[],System-Int32,System-Int32-'></a>
### Read() `method`

##### Summary

Reads a block of bytes from the stream and writes the data in a given buffer.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadAsync-System-Byte[],System-Int32,System-Int32-'></a>
### ReadAsync() `method`

##### Summary

Asynchronously reads a sequence of bytes from the current stream and writes the data in a given buffer.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadByte'></a>
### ReadByte() `method`

##### Summary

Reads a byte from the stream and advances the read position one byte.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadLine'></a>
### ReadLine() `method`

##### Summary

Reads a line of characters from the current stream and returns the data as a string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadLineAsync'></a>
### ReadLineAsync() `method`

##### Summary

Reads a line of characters asynchronously from the current stream and returns the data as a string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadToEnd'></a>
### ReadToEnd() `method`

##### Summary

Reads all characters from the current position to the end of the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-ReadToEndAsync'></a>
### ReadToEndAsync() `method`

##### Summary

Reads all characters from the current position to the end of the stream asynchronously and returns them as one string.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-Byte[],System-Int32,System-Int32-'></a>
### Write() `method`

##### Summary

Writes a block of bytes to the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-String-'></a>
### Write() `method`

##### Summary

Writes a string to the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-Write-System-Object-'></a>
### Write() `method`

##### Summary

Writes the text representation of an object to the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-Byte[],System-Int32,System-Int32-'></a>
### WriteAsync() `method`

##### Summary

Asynchronously writes a sequence of bytes to the current stream and advances the current position.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-String-'></a>
### WriteAsync() `method`

##### Summary

Asynchronously writes a string to the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteAsync-System-Object-'></a>
### WriteAsync() `method`

##### Summary

Asynchronously writes the text representation of an object to the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteByte-System-Byte-'></a>
### WriteByte() `method`

##### Summary

Writes a byte to the current position in the stream.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteLine-System-String-'></a>
### WriteLine() `method`

##### Summary

Writes a string to the stream, followed by a line terminator.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteLine-System-Object-'></a>
### WriteLine() `method`

##### Summary

Writes the text representation of an object to the stream, followed by a line terminator.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteLineAsync-System-String-'></a>
### WriteLineAsync() `method`

##### Summary

Asynchronously writes a string to the stream, followed by a line terminator.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-Services-IO-IStream-WriteLineAsync-System-Object-'></a>
### WriteLineAsync() `method`

##### Summary

Asynchronously writes the text representation of an object to the stream, followed by a line terminator.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Events-IncomingChatMessageEventArgs'></a>
## IncomingChatMessageEventArgs `type`

##### Namespace

Obsidian.API.Events

<a name='P-Obsidian-API-Events-IncomingChatMessageEventArgs-Message'></a>
### Message `property`

##### Summary

The message that was sent.

<a name='T-Obsidian-API-Plugins-InjectAttribute'></a>
## InjectAttribute `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Indicates that the property should be injected with a service.

<a name='T-Obsidian-API-Events-InventoryClickEventArgs'></a>
## InventoryClickEventArgs `type`

##### Namespace

Obsidian.API.Events

<a name='P-Obsidian-API-Events-InventoryClickEventArgs-Inventory'></a>
### Inventory `property`

##### Summary

Gets the clicked inventory

<a name='P-Obsidian-API-Events-InventoryClickEventArgs-Item'></a>
### Item `property`

##### Summary

Gets the current item that was clicked

<a name='P-Obsidian-API-Events-InventoryClickEventArgs-Slot'></a>
### Slot `property`

##### Summary

Gets the slot that was clicked

<a name='T-Obsidian-API-Events-PlayerEventArgs'></a>
## PlayerEventArgs `type`

##### Namespace

Obsidian.API.Events

<a name='P-Obsidian-API-Events-PlayerEventArgs-Player'></a>
### Player `property`

##### Summary

The player involved in this event

<a name='T-Obsidian-API-Events-PlayerJoinEventArgs'></a>
## PlayerJoinEventArgs `type`

##### Namespace

Obsidian.API.Events

<a name='P-Obsidian-API-Events-PlayerJoinEventArgs-JoinDate'></a>
### JoinDate `property`

##### Summary

The date the player joined.

<a name='T-Obsidian-API-Plugins-PluginAttribute'></a>
## PluginAttribute `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Provides information about the plugin.

<a name='P-Obsidian-API-Plugins-PluginAttribute-Authors'></a>
### Authors `property`

##### Summary

Name(s) of the plugin's author(s).

<a name='P-Obsidian-API-Plugins-PluginAttribute-Description'></a>
### Description `property`

##### Summary

Description of the plugin.

<a name='P-Obsidian-API-Plugins-PluginAttribute-Name'></a>
### Name `property`

##### Summary

Name of the plugin.

<a name='P-Obsidian-API-Plugins-PluginAttribute-ProjectUrl'></a>
### ProjectUrl `property`

##### Summary

URL address of where the plugin is hosted.

<a name='P-Obsidian-API-Plugins-PluginAttribute-Version'></a>
### Version `property`

##### Summary

Version of the plugin. The string should contain the major, minor, , and numbers, split by a period character ('.').

<a name='T-Obsidian-API-Plugins-PluginBase'></a>
## PluginBase `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Provides the base class for a plugin.

<a name='M-Obsidian-API-Plugins-PluginBase-FriendlyInvokeAsync-System-String,System-Object[]-'></a>
### FriendlyInvokeAsync() `method`

##### Summary

Invokes a method in the class. The actual method can accept less parameters than `args`.
If exception occurs, it is returned inside [AggregateException](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.AggregateException 'System.AggregateException').
This method can be used on non-async methods too.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]-'></a>
### GetMethod\`\`1() `method`

##### Summary

Returns a delegate for this plugin's method.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-GetPropertyGetter``1-System-String-'></a>
### GetPropertyGetter\`\`1() `method`

##### Summary

Returns a delegate for this plugin's property getter.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-GetPropertySetter``1-System-String-'></a>
### GetPropertySetter\`\`1() `method`

##### Summary

Returns a delegate for this plugin's property setter.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-Invoke-System-String,System-Object[]-'></a>
### Invoke() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginBase.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-InvokeAsync-System-String,System-Object[]-'></a>
### InvokeAsync() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginBase.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.
This method can be used on non-async methods too.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-InvokeAsync``1-System-String,System-Object[]-'></a>
### InvokeAsync\`\`1() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginBase.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.
This method can be used on non-async methods too.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-Invoke``1-System-String,System-Object[]-'></a>
### Invoke\`\`1() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginBase-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginBase.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginBase-Unload'></a>
### Unload() `method`

##### Summary

Causes this plugin to be unloaded.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Plugins-PluginPermissions'></a>
## PluginPermissions `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Represents permissions for performing specific types of actions.

<a name='F-Obsidian-API-Plugins-PluginPermissions-CanRead'></a>
### CanRead `constants`

##### Summary

Allows reading from files.

<a name='F-Obsidian-API-Plugins-PluginPermissions-CanWrite'></a>
### CanWrite `constants`

##### Summary

Allows writing to files.

<a name='F-Obsidian-API-Plugins-PluginPermissions-Compilation'></a>
### Compilation `constants`

##### Summary

Allows using Microsoft.CodeAnalysis and related libraries.

<a name='F-Obsidian-API-Plugins-PluginPermissions-FileAccess'></a>
### FileAccess `constants`

##### Summary

Allows working with files.

<a name='F-Obsidian-API-Plugins-PluginPermissions-Interop'></a>
### Interop `constants`

##### Summary

Allows using native libraries.

<a name='F-Obsidian-API-Plugins-PluginPermissions-NetworkAccess'></a>
### NetworkAccess `constants`

##### Summary

Allows doing actions over network.

<a name='F-Obsidian-API-Plugins-PluginPermissions-Reflection'></a>
### Reflection `constants`

##### Summary

Allows performing reflection.

<a name='F-Obsidian-API-Plugins-PluginPermissions-RunningSubprocesses'></a>
### RunningSubprocesses `constants`

##### Summary

Allows using System.Diagnostics and System.Runtime.Loader libraries.

<a name='F-Obsidian-API-Plugins-PluginPermissions-ThirdPartyLibraries'></a>
### ThirdPartyLibraries `constants`

##### Summary

Allows using 3rd party libraries.

<a name='T-Obsidian-API-Plugins-PluginWrapper'></a>
## PluginWrapper `type`

##### Namespace

Obsidian.API.Plugins

##### Summary

Provides the base class for a plugin wrapper.

<a name='M-Obsidian-API-Plugins-PluginWrapper-GetMethod``1-System-String,System-Type[]-'></a>
### GetMethod\`\`1() `method`

##### Summary

Returns a delegate for this plugin's method.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginWrapper-GetPropertyGetter``1-System-String-'></a>
### GetPropertyGetter\`\`1() `method`

##### Summary

Returns a delegate for this plugin's property getter.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginWrapper-GetPropertySetter``1-System-String-'></a>
### GetPropertySetter\`\`1() `method`

##### Summary

Returns a delegate for this plugin's property setter.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginWrapper-Invoke-System-String,System-Object[]-'></a>
### Invoke() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginWrapper-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginWrapper.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Plugins-PluginWrapper-Invoke``1-System-String,System-Object[]-'></a>
### Invoke\`\`1() `method`

##### Summary

Invokes a method in the class. For repeated calls use [GetMethod\`\`1](#M-Obsidian-API-Plugins-PluginWrapper-GetMethod``1-System-String,System-Type[]- 'Obsidian.API.Plugins.PluginWrapper.GetMethod``1(System.String,System.Type[])') or make a plugin wrapper.

##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-Position'></a>
## Position `type`

##### Namespace

Obsidian.API

##### Summary

Represents position in three-dimensional space. Uses [Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32').

<a name='M-Obsidian-API-Position-#ctor-System-Int32-'></a>
### #ctor(value) `constructor`

##### Summary

Creates new instance of [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') with [X](#P-Obsidian-API-Position-X 'Obsidian.API.Position.X'), [Y](#P-Obsidian-API-Position-Y 'Obsidian.API.Position.Y') and [Z](#P-Obsidian-API-Position-Z 'Obsidian.API.Position.Z') set to `value`.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| value | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of [X](#P-Obsidian-API-Position-X 'Obsidian.API.Position.X'), [Y](#P-Obsidian-API-Position-Y 'Obsidian.API.Position.Y') and [Z](#P-Obsidian-API-Position-Z 'Obsidian.API.Position.Z'). |

<a name='M-Obsidian-API-Position-#ctor-System-Int32,System-Int32,System-Int32-'></a>
### #ctor(x,y,z) `constructor`

##### Summary

Creates a new instance of [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') with specific values.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of X coordinate. |
| y | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of Y coordinate. |
| z | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of Z coordinate. |

<a name='P-Obsidian-API-Position-Magnitude'></a>
### Magnitude `property`

##### Summary

Calculates distance of this [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') from [Zero](#F-Obsidian-API-Position-Zero 'Obsidian.API.Position.Zero').

<a name='P-Obsidian-API-Position-X'></a>
### X `property`

##### Summary

X component of the [Position](#T-Obsidian-API-Position 'Obsidian.API.Position').

<a name='P-Obsidian-API-Position-Y'></a>
### Y `property`

##### Summary

Y component of the [Position](#T-Obsidian-API-Position 'Obsidian.API.Position').

<a name='P-Obsidian-API-Position-Z'></a>
### Z `property`

##### Summary

Z component of the [Position](#T-Obsidian-API-Position 'Obsidian.API.Position').

<a name='M-Obsidian-API-Position-ChunkClamp'></a>
### ChunkClamp() `method`

##### Summary

Returns [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') clamped to fit inside a single minecraft chunk.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Position-Clamp-Obsidian-API-Position,Obsidian-API-Position-'></a>
### Clamp() `method`

##### Summary

Returns [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') clamped to the inclusive range of `min` and `max`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Position-DistanceTo-Obsidian-API-Position,Obsidian-API-Position-'></a>
### DistanceTo() `method`

##### Summary

Calculates the distance between two [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') objects.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Position-Equals-Obsidian-API-Position-'></a>
### Equals() `method`

##### Summary

Indicates whether this [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') is near equal to `other`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Position-Square-System-Int32-'></a>
### Square() `method`

##### Summary

Calculates the square of a `number`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-Position-ToString'></a>
### ToString() `method`

##### Summary

Returns [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') formatted as a [String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String').

##### Returns



##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-PositionF'></a>
## PositionF `type`

##### Namespace

Obsidian.API

##### Summary

Represents position in three-dimensional space. Uses [Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single').

<a name='M-Obsidian-API-PositionF-#ctor-System-Single-'></a>
### #ctor(value) `constructor`

##### Summary

Creates new instance of [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') with [X](#P-Obsidian-API-PositionF-X 'Obsidian.API.PositionF.X'), [Y](#P-Obsidian-API-PositionF-Y 'Obsidian.API.PositionF.Y') and [Z](#P-Obsidian-API-PositionF-Z 'Obsidian.API.PositionF.Z') set to `value`.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| value | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | Value of [X](#P-Obsidian-API-PositionF-X 'Obsidian.API.PositionF.X'), [Y](#P-Obsidian-API-PositionF-Y 'Obsidian.API.PositionF.Y') and [Z](#P-Obsidian-API-PositionF-Z 'Obsidian.API.PositionF.Z'). |

<a name='M-Obsidian-API-PositionF-#ctor-System-Int32,System-Int32,System-Int32-'></a>
### #ctor(x,y,z) `constructor`

##### Summary

Creates a new instance of [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') with specific values.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of X coordinate. |
| y | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of Y coordinate. |
| z | [System.Int32](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int32 'System.Int32') | Value of Z coordinate. |

<a name='M-Obsidian-API-PositionF-#ctor-System-Single,System-Single,System-Single-'></a>
### #ctor(x,y,z) `constructor`

##### Summary

Creates a new instance of [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') with specific values.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | Value of X coordinate. |
| y | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | Value of Y coordinate. |
| z | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | Value of Z coordinate. |

<a name='P-Obsidian-API-PositionF-Magnitude'></a>
### Magnitude `property`

##### Summary

Calculates distance of this [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') from [Zero](#F-Obsidian-API-PositionF-Zero 'Obsidian.API.PositionF.Zero').

<a name='P-Obsidian-API-PositionF-X'></a>
### X `property`

##### Summary

X component of the [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF').

<a name='P-Obsidian-API-PositionF-Y'></a>
### Y `property`

##### Summary

Y component of the [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF').

<a name='P-Obsidian-API-PositionF-Z'></a>
### Z `property`

##### Summary

Z component of the [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF').

<a name='M-Obsidian-API-PositionF-ChunkClamp'></a>
### ChunkClamp() `method`

##### Summary

Returns [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') clamped to fit inside a single minecraft chunk.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-Clamp-Obsidian-API-PositionF,Obsidian-API-PositionF-'></a>
### Clamp() `method`

##### Summary

Returns [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') clamped to the inclusive range of `min` and `max`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-DistanceTo-Obsidian-API-PositionF,Obsidian-API-PositionF-'></a>
### DistanceTo() `method`

##### Summary

Calculates the distance between two [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') objects.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-Equals-Obsidian-API-PositionF-'></a>
### Equals() `method`

##### Summary

Indicates whether this [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') is near equal to `other`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-Floor'></a>
### Floor() `method`

##### Summary

Truncates the decimal component of each part of this [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF').

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-Normalize'></a>
### Normalize() `method`

##### Summary

Performs vector normalization on this [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF')'s coordinates.

##### Returns

Normalized [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF').

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-Square-System-Single-'></a>
### Square() `method`

##### Summary

Calculates the square of a `number`.

##### Parameters

This method has no parameters.

<a name='M-Obsidian-API-PositionF-ToString'></a>
### ToString() `method`

##### Summary

Returns [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') formatted as a [String](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.String 'System.String').

##### Returns



##### Parameters

This method has no parameters.

<a name='T-Obsidian-API-SoundCategory'></a>
## SoundCategory `type`

##### Namespace

Obsidian.API

##### Summary

https://gist.github.com/konwboj/7c0c380d3923443e9d55

<a name='T-Obsidian-API-Velocity'></a>
## Velocity `type`

##### Namespace

Obsidian.API

##### Summary

Represents velocity of an entity in the world.

<a name='M-Obsidian-API-Velocity-#ctor-System-Int16,System-Int16,System-Int16-'></a>
### #ctor(x,y,z) `constructor`

##### Summary

Creates a new instance of [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity') with specific values.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Int16](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int16 'System.Int16') | Velocity on the X axis. |
| y | [System.Int16](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int16 'System.Int16') | Velocity on the Y axis. |
| z | [System.Int16](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Int16 'System.Int16') | Velocity on the Z axis. |

<a name='P-Obsidian-API-Velocity-Magnitude'></a>
### Magnitude `property`

##### Summary

Returns the length of this [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity').

<a name='P-Obsidian-API-Velocity-X'></a>
### X `property`

##### Summary

Velocity on the X axis.

<a name='P-Obsidian-API-Velocity-Y'></a>
### Y `property`

##### Summary

Velocity on the Y axis.

<a name='P-Obsidian-API-Velocity-Z'></a>
### Z `property`

##### Summary

Velocity on the Z axis.

<a name='M-Obsidian-API-Velocity-FromBlockPerSecond-System-Single,System-Single,System-Single-'></a>
### FromBlockPerSecond(x,y,z) `method`

##### Summary

Returns [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity') expressed as how many blocks on each axis can be travelled in a second.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the X axis in a second. |
| y | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the Y axis in a second. |
| z | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the Z axis in a second. |

<a name='M-Obsidian-API-Velocity-FromBlockPerTick-System-Single,System-Single,System-Single-'></a>
### FromBlockPerTick(x,y,z) `method`

##### Summary

Returns [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity') expressed as how many blocks on each axis can be travelled in a tick (50ms).

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| x | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the X axis in a tick (50ms). |
| y | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the Y axis in a tick (50ms). |
| z | [System.Single](http://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k:System.Single 'System.Single') | How many blocks can be travelled on the Z axis in a tick (50ms). |

<a name='M-Obsidian-API-Velocity-FromDirection-Obsidian-API-Position,Obsidian-API-Position-'></a>
### FromDirection(from,to) `method`

##### Summary

Returns such velocity, that can travel from `from` to `to` in a second.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| from | [Obsidian.API.Position](#T-Obsidian-API-Position 'Obsidian.API.Position') | Starting position. |
| to | [Obsidian.API.Position](#T-Obsidian-API-Position 'Obsidian.API.Position') | Target position. |

<a name='M-Obsidian-API-Velocity-FromDirection-Obsidian-API-PositionF,Obsidian-API-PositionF-'></a>
### FromDirection(from,to) `method`

##### Summary

Returns such velocity, that can travel from `from` to `to` in a second.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| from | [Obsidian.API.PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') | Starting position. |
| to | [Obsidian.API.PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') | Target position. |

<a name='M-Obsidian-API-Velocity-FromPosition-Obsidian-API-Position-'></a>
### FromPosition(position) `method`

##### Summary

Turns [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') into [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity'), using it's coordinates as to how many blocks can be travelled per second.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| position | [Obsidian.API.Position](#T-Obsidian-API-Position 'Obsidian.API.Position') | [Position](#T-Obsidian-API-Position 'Obsidian.API.Position') to be used for conversion. |

<a name='M-Obsidian-API-Velocity-FromPosition-Obsidian-API-PositionF-'></a>
### FromPosition(position) `method`

##### Summary

Turns [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') into [Velocity](#T-Obsidian-API-Velocity 'Obsidian.API.Velocity'), using it's coordinates as to how many blocks can be travelled per second.

##### Parameters

| Name | Type | Description |
| ---- | ---- | ----------- |
| position | [Obsidian.API.PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') | [PositionF](#T-Obsidian-API-PositionF 'Obsidian.API.PositionF') to be used for conversion. |
