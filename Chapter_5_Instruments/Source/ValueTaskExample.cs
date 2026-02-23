namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class ValueTaskExample
    {
        private static bool _calledOnce;
        
        public static async Task RunValueTaskExample()
        {
            for (int i = 0; i < 100; i++)
            {
                var t = OptimizedDoWork();
                await t;
            }
        }

        private static ValueTask OptimizedDoWork()
        {
            if (!_calledOnce)
            {
                _calledOnce = true;
                return new ValueTask(ShouldDoWorkOnce());
            }
            
            return ValueTask.CompletedTask;
        }

        private static async Task ShouldDoWorkOnce()
        {
            await Task.Delay(100);
            Console.WriteLine("Hello World!");
        }
    }
}