namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TaskWhenAllSynchronousWaitExample
    {
        public static async Task WhenAllExceptionsExample()
        {
            var t1 = Exception1();
            var t2 = Exception2();
            var t3 = Exception3();
            
            var allTasks = Task.WhenAll(t1, t2, t3);
            try
            {
                await allTasks;
            }
            catch
            {
                // Ничего не делаем с исключениями, чтобы дальше вызвать Wait()
            }

            allTasks.Wait();
        }

        private static async Task Exception1()
        {
            throw new Exception("Exception1");
        }
        
        private static async Task Exception2()
        {
            throw new Exception("Exception2");
        }
        
        private static async Task Exception3()
        {
            throw new Exception("Exception3");
        }
    }
}