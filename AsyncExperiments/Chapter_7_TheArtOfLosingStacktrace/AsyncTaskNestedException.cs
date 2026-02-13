namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace
{
    public static class AsyncTaskNestedException
    {
        public static async Task AsyncTaskExceptionTest()
        {
            TaskScheduler.UnobservedTaskException += (sender, args) => Console.WriteLine($"Unobserved task exception {args.Exception.Flatten()}");
            await Layer0();
            await Task.Delay(1000);
            GC.Collect();
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            await Layer2();
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            await Layer4();
        }
        
        private static async Task Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}