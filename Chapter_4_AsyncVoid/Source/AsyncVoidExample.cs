namespace AsyncResearch.Chapter_4_AsyncVoid.Source
{
    public static class AsyncVoidExample
    {
        public static void Test()
        {
            Console.WriteLine("Start");
            DoWorkAsync();
            Console.WriteLine("End");
        }
        
        public static async void DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(10000);
            Console.WriteLine("Resumed with " + localVariable);
        }
    }
}