namespace AsyncResearch.AsyncExperiments.Chapter_6_AsyncAndGarbageCollector.Source
{
    public static class OrchestratingMemoryLeak
    {
        private static List<TaskCompletionSource<bool>> _tcsList = [];

        public static void Test()
        {
            Console.WriteLine($"Start: {GC.GetTotalMemory(true) / 1024} KB");
            
            Leak();
            
            Console.WriteLine($"After Leak: {GC.GetTotalMemory(false) / 1024} KB");
            
            for (int i = 0; i < 5; i++)
            {
                Thread.Sleep(1000);
                Console.WriteLine($"Tick {i}: {GC.GetTotalMemory(true) / 1024} KB");
            }
        }

        private static void Leak()
        {
            var tcs = new TaskCompletionSource<bool>();
            _tcsList.Add(tcs);
        
            MyMethod(tcs.Task);
            tcs.TrySetResult(true);
        }
        
        private static async void MyMethod(Task<bool> task) 
        {
            var hugeData = new byte[1024 * 1024 * 100]; // 100 MB
            await task; 
            Console.WriteLine(hugeData.Length);
        }
    }
}