using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PokeTokenBar.Core;
using PokeTokenBar.Platform.Windows;

var root=Path.GetFullPath("Windows/artifacts/update-integration/run 검증-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
File.WriteAllText("Windows/artifacts/update-integration/last-run.txt",root);
Environment.SetEnvironmentVariable("PTB_UPDATE_TEST_ROOT",root);
var install=Path.Combine(root,"installed app 'quote'");Directory.CreateDirectory(install);
var template=Path.Combine(root,"template");Directory.CreateDirectory(template);
var data=Path.Combine(root,"data");Directory.CreateDirectory(data);
foreach(var file in Directory.GetFiles("Windows/tests/UpdateIntegration/Target/bin/Release/net10.0")){
    File.Copy(file,Path.Combine(install,Path.GetFileName(file)),true);
    File.Copy(file,Path.Combine(template,Path.GetFileName(file)),true);
}
var executable=Path.Combine(install,"PokeTokenBar.Windows.exe");File.Copy(Path.Combine(install,"Target.exe"),executable,true);
File.WriteAllText(Path.Combine(install,"unins000.exe"),"test marker");
File.WriteAllText(Path.Combine(data,"companion-state.json"),"{\"beforeExit\":true}");
File.WriteAllText(Path.Combine(data,"settings.json"),"{\"growthDifficulty\":0.75}");
var installerSource=Path.GetFullPath("Windows/tests/UpdateIntegration/Installer/bin/Release/net10.0/Installer.exe");
var installerDirectory=Path.GetDirectoryName(installerSource)!;
// App-host dependencies travel with this controlled test package only.
using var http=new HttpClient(new FixtureHandler(File.ReadAllBytes(installerSource)));
var client=new AppUpdateClient(http);
var release=(await client.CheckAsync(new Version(1,0,0)))!;
var package=await client.DownloadAsync(release,Path.Combine(root,"Downloads"));
foreach(var file in Directory.GetFiles(installerDirectory).Where(f=>!f.EndsWith(".exe")))File.Copy(file,Path.Combine(Path.GetDirectoryName(package)!,Path.GetFileName(file)),true);
using var oldProcess=Process.Start(new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,ArgumentList={"--old"}})!;
await WaitFile("old-started.txt");
await WindowsUpdateInstaller.LaunchAsync(package,release,executable,data,oldProcess.Id);
await Task.Delay(750);
if(File.Exists(Path.Combine(root,"installer-ran.txt")))throw new Exception("Helper did not wait for graceful exit");
File.WriteAllText(Path.Combine(root,"permit-exit.txt"),"go");
await WaitFile("restarted.txt");
var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(package)!,"update-result.json")));
if(!result.RootElement.GetProperty("success").GetBoolean())throw new Exception(result.RootElement.ToString());
var backup=result.RootElement.GetProperty("backup").GetString()!;
if(File.ReadAllText(Path.Combine(backup,"companion-state.json"))!=File.ReadAllText(Path.Combine(data,"companion-state.json")))throw new Exception("Save backup differs");
if(File.ReadAllText(Path.Combine(data,"settings.json"))!="{\"growthDifficulty\":0.75}")throw new Exception("Settings changed");
var script=File.ReadAllText(Path.Combine(Path.GetDirectoryName(package)!,"apply-update.ps1"));
File.WriteAllText("Windows/artifacts/update-integration/result.txt","PASS: public-client metadata/download/SHA, detached helper readiness, live-process wait, final-exit save backup, quoted/unicode install path, installer arguments, data preservation and restart.\n");
Console.WriteLine("Update helper integration PASS");
async Task WaitFile(string name){var timeout=DateTime.UtcNow.AddSeconds(40);while(!File.Exists(Path.Combine(root,name))){if(DateTime.UtcNow>timeout)throw new Exception("Timed out: "+name);await Task.Delay(100);}}

class FixtureHandler(byte[] installer):HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        var name="PokeTokenBar-1.0.1-win-x64-setup.exe";var prefix="https://github.com/rowlet9g/PokeTokenBar-Windows/releases/download/v1.0.1/";
        var url=request.RequestUri!.AbsoluteUri;HttpContent content;
        if(url.EndsWith("/latest"))content=new StringContent(JsonSerializer.Serialize(new{tag_name="v1.0.1",draft=false,prerelease=false,
            assets=new[]{name,"release-manifest.json","SHA256SUMS.txt"}.Select(n=>new{name=n,state="uploaded",size=installer.Length,browser_download_url=prefix+n})}));
        else if(url.EndsWith("release-manifest.json"))content=new StringContent(JsonSerializer.Serialize(new{product="PokeTokenBar",version="1.0.1",runtime="win-x64",selfContained=true,installer=name}));
        else if(url.EndsWith("SHA256SUMS.txt"))content=new StringContent(Convert.ToHexString(SHA256.HashData(installer))+" *"+name);
        else content=new ByteArrayContent(installer);
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=content});
    }
}
