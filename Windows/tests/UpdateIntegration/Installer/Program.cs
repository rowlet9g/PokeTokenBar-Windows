var root=Environment.GetEnvironmentVariable("PTB_UPDATE_TEST_ROOT")!;
if(!File.Exists(Path.Combine(root,"old-exited.txt")))throw new Exception("Installer started before old app saved and exited");
File.WriteAllText(Path.Combine(root,"installer-arguments.json"),System.Text.Json.JsonSerializer.Serialize(args));
var directory=args.Single(a=>a.StartsWith("/DIR=",StringComparison.OrdinalIgnoreCase))[5..];
var oldState=File.ReadAllText(Path.Combine(root,"data","companion-state.json"));
if(!oldState.Contains("finalExitSaved"))throw new Exception("Missing final persisted growth");
File.Copy(Path.Combine(root,"template","Target.exe"),Path.Combine(directory,"PokeTokenBar.Windows.exe"),true);
File.WriteAllText(Path.Combine(root,"installer-ran.txt"),directory);
