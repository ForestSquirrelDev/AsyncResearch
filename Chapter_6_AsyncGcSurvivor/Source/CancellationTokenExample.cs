namespace AsyncResearch.Chapter_6_AsyncGcSurvivor.Source
{
    public static class CancellationTokenExample
    {
        private static CancellationTokenSource _cts = new CancellationTokenSource();
        
        public static async Task RunCancellationTokenExample()
        {
            Console.WriteLine($"Start: {GC.GetTotalMemory(true) / 1024} KB");

            await DoWork(_cts.Token);
            
            Console.WriteLine($"After DoWork: {GC.GetTotalMemory(false) / 1024} KB");

            GcCollect();
            
            Console.WriteLine($"After GC collect (LEAK): {GC.GetTotalMemory(false) / 1024} KB");
            
            await _cts.CancelAsync();
            
            GcCollect();
            Console.WriteLine($"After cancel: {GC.GetTotalMemory(false) / 1024} KB");
        }

        private static async Task DoWork(CancellationToken token)
        {
            var heavyData = new byte[1024 * 1024 * 100];
    
            token.Register(() => Console.WriteLine($"Галя, у нас отмена! {heavyData.Length}")); 
    
            await Task.Delay(1000, token);
            
            Console.WriteLine($"Heavy data: {heavyData.Length}");
        }

        private static void GcCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}