var root=Environment.GetEnvironmentVariable("PTB_UPDATE_TEST_ROOT")!;
if(args.Contains("--old")){
    File.WriteAllText(Path.Combine(root,"old-started.txt"),Environment.ProcessId.ToString());
    while(!File.Exists(Path.Combine(root,"permit-exit.txt")))Thread.Sleep(100);
    File.WriteAllText(Path.Combine(root,"data","companion-state.json"),"{\"finalExitSaved\":true,\"species\":906,\"credits\":123456789}");
    File.WriteAllText(Path.Combine(root,"old-exited.txt"),DateTime.UtcNow.ToString("O"));
}else File.WriteAllText(Path.Combine(root,"restarted.txt"),Environment.ProcessPath);
