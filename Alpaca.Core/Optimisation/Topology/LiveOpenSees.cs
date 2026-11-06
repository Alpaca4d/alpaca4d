using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Alpaca4d.Optimisation
{
    /// <summary>
    /// One OpenSees that stays open and takes commands, so a model is built once and solved many
    /// times. Starting OpenSees and building the model again for every iteration of an optimisation
    /// costs more than the solve itself on all but the largest models.
    ///
    /// The deck ends in a loop that reads a line from standard input, evaluates it, and prints
    /// <see cref="Done"/>, or <see cref="Failed"/> and the error, on standard error - see
    /// <see cref="CommandLoop"/>. Standard error because OpenSees writes it unbuffered: standard
    /// output into a pipe is buffered, and the reply would sit there until the buffer filled.
    /// </summary>
    public sealed class LiveOpenSees : IDisposable
    {
        public const string Done = "ALPACA_DONE";
        public const string Failed = "ALPACA_FAILED";

        /// <summary>Lines OpenSees prints for every element of some kinds, which say nothing worth keeping.</summary>
        private static readonly string[] Chatter = { "Brick::constructor", "SSPbrick::", "FourNodeTetrahedron::" };

        /// <summary>How much of what OpenSees printed <see cref="Log"/> keeps, from the end.</summary>
        private const int LogLimit = 20000;

        private readonly Process _process;
        private readonly StringBuilder _log = new StringBuilder();
        private readonly object _gate = new object();
        private readonly AutoResetEvent _replied = new AutoResetEvent(false);
        private readonly TimeSpan? _timeout;
        private string _reply;

        /// <summary>
        /// The Tcl that ends a deck run this way. A command that fails is caught, so OpenSees stays
        /// open and the error comes back as the reply; "exit" ends the loop, and OpenSees with it.
        /// </summary>
        public const string CommandLoop =
@"proc alpacaReply {command} {
    if {[catch {uplevel #0 $command} message]} {
        regsub -all {\s+} $message { } message
        puts stderr ""ALPACA_FAILED $message""
    } else {
        puts stderr ""ALPACA_DONE""
    }
    flush stderr
}
while {[gets stdin alpacaLine] >= 0} {
    if {$alpacaLine eq ""exit""} break
    alpacaReply $alpacaLine
}
";

        /// <summary>
        /// Starts OpenSees on <paramref name="deck"/>, which must end in <see cref="CommandLoop"/>, and
        /// waits until it has built the model. Throws, with what OpenSees printed, when it cannot.
        /// </summary>
        public LiveOpenSees(string openSees, string deck, TimeSpan? timeout)
        {
            _timeout = timeout;
            _process = new Process();
            _process.StartInfo.FileName = openSees;
            _process.StartInfo.Arguments = "\"" + deck + "\"";
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.OutputDataReceived += (sender, e) => Heard(e.Data);
            _process.ErrorDataReceived += (sender, e) => Heard(e.Data);

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            _process.StandardInput.AutoFlush = true;

            // Anything that answers proves the deck was read to its loop.
            try
            {
                string problem = Send("set alpacaReady 1");
                if (problem != null)
                    throw new InvalidOperationException("OpenSees could not build the model: " + problem);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Everything OpenSees printed, less the per-element chatter, the end of it when long.</summary>
        public string Log
        {
            get
            {
                lock (_gate)
                    return _log.ToString().Trim();
            }
        }

        /// <summary>
        /// Evaluates one line of Tcl and waits for it: null when it ran, its error message when it
        /// failed. Throws when OpenSees has gone, or takes longer than the timeout.
        /// </summary>
        public string Send(string command)
        {
            if (command.IndexOf('\n') >= 0 || command.IndexOf('\r') >= 0)
                throw new ArgumentException("One line at a time.");

            lock (_gate)
                _reply = null;
            _replied.Reset();

            try
            {
                _process.StandardInput.WriteLine(command);
            }
            catch (System.IO.IOException)
            {
                throw Gone();
            }

            var clock = Stopwatch.StartNew();
            while (true)
            {
                if (_replied.WaitOne(100))
                {
                    lock (_gate)
                        return _reply.Length == 0 ? null : _reply;
                }

                if (_process.HasExited)
                {
                    // The reply may have been the last thing it printed.
                    _process.WaitForExit();
                    lock (_gate)
                        if (_reply != null)
                            return _reply.Length == 0 ? null : _reply;
                    throw Gone();
                }

                if (_timeout.HasValue && clock.Elapsed > _timeout.Value)
                {
                    Kill();
                    throw new TimeoutException($"OpenSees did not answer within {_timeout.Value} and was stopped.\n" + Log);
                }
            }
        }

        private Exception Gone()
        {
            return new InvalidOperationException("OpenSees stopped. What it printed:\n" + Log);
        }

        private void Heard(string line)
        {
            if (line == null)
                return;

            lock (_gate)
            {
                if (line.StartsWith(Done, StringComparison.Ordinal))
                {
                    _reply = "";
                    _replied.Set();
                    return;
                }

                if (line.StartsWith(Failed, StringComparison.Ordinal))
                {
                    _reply = line.Substring(Failed.Length).Trim();
                    if (_reply.Length == 0)
                        _reply = "failed";
                    _replied.Set();
                    return;
                }

                foreach (var chatter in Chatter)
                    if (line.StartsWith(chatter, StringComparison.Ordinal))
                        return;

                _log.AppendLine(line);
                if (_log.Length > 2 * LogLimit)
                    _log.Remove(0, _log.Length - LogLimit);
            }
        }

        private void Kill()
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        /// <summary>Ends the loop, and OpenSees with it; stops it if it does not go.</summary>
        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.StandardInput.WriteLine("exit");
                    if (!_process.WaitForExit(5000))
                        Kill();
                }
            }
            catch (Exception)
            {
                Kill();
            }
            finally
            {
                _process.Dispose();
                _replied.Dispose();
            }
        }
    }
}
