using System;
using System.Threading;
using System.Threading.Tasks;
using PainelSeguranca.Core;

namespace PainelSeguranca.Services
{
    /// <summary>
    /// Pausa temporaria da protecao em tempo real.
    ///
    /// O Defender NAO tem "pausar por X minutos" nativo. Implementamos assim:
    /// desligar a protecao em tempo real (sujeito ao Tamper Protection) + um timer interno
    /// que RELIGA ao fim do periodo, com contagem regressiva. "Ate reiniciar" conta com o
    /// boot reativar a protecao (a tarefa/servico do Defender religa no proximo logon/boot).
    ///
    /// Singleton de aplicacao. Os eventos sao disparados em thread de timer; a UI deve
    /// marshalar para a thread de interface (Invoke).
    /// </summary>
    public sealed class PauseProtectionService
    {
        public static readonly PauseProtectionService Instance = new PauseProtectionService();

        private readonly object _gate = new object();
        private Timer _timer;
        private DateTime? _resumeAtUtc;
        private bool _untilReboot;

        /// <summary>Disparado a cada segundo enquanto pausado e nas mudancas de estado.</summary>
        public event Action Changed;

        private PauseProtectionService() { }

        public bool IsPaused
        {
            get { lock (_gate) return _resumeAtUtc.HasValue || _untilReboot; }
        }

        public bool UntilReboot
        {
            get { lock (_gate) return _untilReboot; }
        }

        public TimeSpan TimeRemaining
        {
            get
            {
                lock (_gate)
                {
                    if (!_resumeAtUtc.HasValue) return TimeSpan.Zero;
                    var rem = _resumeAtUtc.Value - DateTime.UtcNow;
                    return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
                }
            }
        }

        /// <summary>Pausa por uma duracao definida e religa automaticamente ao fim.</summary>
        public async Task<OperationResult> PauseForAsync(TimeSpan duration)
        {
            var res = await DefenderService.SetRealtimeProtectionAsync(false);
            if (!res.Success) return res;

            lock (_gate)
            {
                _untilReboot = false;
                _resumeAtUtc = DateTime.UtcNow.Add(duration);
                StartTimerLocked();
            }
            Logger.Info("Protecao pausada por " + duration);
            RaiseChanged();
            return OperationResult.Ok();
        }

        /// <summary>Pausa ate o proximo reinicio (sem timer de religar).</summary>
        public async Task<OperationResult> PauseUntilRebootAsync()
        {
            var res = await DefenderService.SetRealtimeProtectionAsync(false);
            if (!res.Success) return res;

            lock (_gate)
            {
                StopTimerLocked();
                _resumeAtUtc = null;
                _untilReboot = true;
            }
            Logger.Info("Protecao pausada ate reiniciar.");
            RaiseChanged();
            return OperationResult.Ok();
        }

        /// <summary>Retoma a protecao imediatamente.</summary>
        public async Task<OperationResult> ResumeAsync()
        {
            lock (_gate)
            {
                StopTimerLocked();
                _resumeAtUtc = null;
                _untilReboot = false;
            }
            var res = await DefenderService.SetRealtimeProtectionAsync(true);
            Logger.Info("Protecao retomada (sucesso=" + res.Success + ").");
            RaiseChanged();
            return res;
        }

        private void StartTimerLocked()
        {
            StopTimerLocked();
            _timer = new Timer(OnTick, null, 1000, 1000);
        }

        private void StopTimerLocked()
        {
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
            }
        }

        private void OnTick(object state)
        {
            bool expired;
            lock (_gate)
            {
                expired = _resumeAtUtc.HasValue && DateTime.UtcNow >= _resumeAtUtc.Value;
            }

            if (expired)
            {
                // Tempo acabou: religa a protecao.
                _ = AutoResume();
            }
            else
            {
                RaiseChanged();
            }
        }

        private async Task AutoResume()
        {
            lock (_gate)
            {
                StopTimerLocked();
                _resumeAtUtc = null;
                _untilReboot = false;
            }
            var res = await DefenderService.SetRealtimeProtectionAsync(true);
            Logger.Info("Pausa expirou; protecao religada automaticamente (sucesso=" + res.Success + ").");
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var h = Changed;
            if (h != null)
            {
                try { h(); } catch (Exception ex) { Logger.Error("Erro no handler de pausa", ex); }
            }
        }
    }
}
