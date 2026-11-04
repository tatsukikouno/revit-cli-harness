using System;
using System.Threading.Tasks;

namespace RevitHarness.Dialogs
{
    // Runs a UI Automation press with a time limit. Only a call that returned counts as a press.
    public static class InvokeGuard
    {
        // Returns null when the action completed, otherwise why it is not known to have completed.
        public static string? Run(Action action, TimeSpan timeout)
        {
            var task = Task.Run(action);
            try
            {
                if (!task.Wait(timeout)) return "invoke did not return within " + (int)timeout.TotalSeconds + " s; outcome unknown";
                return null;
            }
            catch (AggregateException e) { return e.InnerException?.Message ?? e.Message; }
        }
    }
}
