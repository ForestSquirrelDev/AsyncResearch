namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class WaitAsyncExample
    {
        public static async Task RunWaitAsyncExample()
        {
            await MyMethod().WaitAsync(TimeSpan.FromSeconds(2));
        }

        private static async Task MyMethod()
        {
            await Task.Delay(3000);
            Console.WriteLine("Hello World!");
        }
    }
}