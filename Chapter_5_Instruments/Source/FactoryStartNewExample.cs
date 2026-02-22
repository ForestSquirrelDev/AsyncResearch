namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class FactoryStartNewExample
    {
        public static async Task RunFactoryStartNewExample()
        {
            var task = Task.Factory.StartNew(async () =>
            {
                await Task.Delay(99999);
                Console.WriteLine("Hello World!");
            });
            await task;
        }
    }
}