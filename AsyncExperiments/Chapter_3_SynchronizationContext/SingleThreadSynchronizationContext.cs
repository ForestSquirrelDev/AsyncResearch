namespace AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext
{
    public class SimpleManualContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback callback, object? state)> _queue = [];
        private readonly List<(SendOrPostCallback callback, object? state)> _currentTickCallbacks = [];
    
        private readonly int _mainThreadId = Environment.CurrentManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                Console.WriteLine($"[SimpleManualContext] Post {d.Method.Name}, state {state}");
                _queue.Add((d, state));
            }
        }

        public void ExecuteTasks()
        {
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException("ExecuteTasks can only be called from the main thread.");
            }

            lock (_queue)
            {
                _currentTickCallbacks.AddRange(_queue);
                _queue.Clear();
            }

            foreach (var work in _currentTickCallbacks)
            {
                work.callback(work.state);
            }
        
            _currentTickCallbacks.Clear();
        }
    }
}