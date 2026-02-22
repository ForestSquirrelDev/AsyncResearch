namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TaskRunException
    {
        public static async Task TaskRunExceptionTest()
        {
            TaskScheduler.UnobservedTaskException += (sender, args) => Console.WriteLine($"Unobserved task exception {args.Exception.Flatten()}");
            _ = Layer0();
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
            Layer4();
        }
        
        private static async void Layer4()
        {
            await Task.Delay(100);
            Task.Run(() =>
            {
                Console.WriteLine("Oh no!");
                throw new Exception("HORY SHIET!");
            });
        }
    }
}