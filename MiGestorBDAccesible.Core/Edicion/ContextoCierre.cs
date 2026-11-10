using System.Collections.Concurrent;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Edicion
{
    public sealed class ContextoCierre
    {
        private static readonly AsyncLocal<ContextoCierre?> _actual = new();

        public static ContextoCierre Actual => _actual.Value ??= new ContextoCierre();

        internal OperacionesBaseDatos Operaciones { get; } = new();

        internal ConcurrentDictionary<ISesionEdicion, byte> Sesiones { get; } = new();

        public bool HayOperacionesEnCurso => Operaciones.HayOperaciones;

        public bool HayCambiosPendientes => Sesiones.Keys.Any(s => s.CambiosPendientes > 0);

        public bool HayCambiosConfirmables => Sesiones.Keys.Any(s => s.CambiosPendientes > 0 && s.PuedeConfirmar);

        public IDisposable Activar()
        {
            var anterior = _actual.Value;
            _actual.Value = this;
            return new Ambito(anterior);
        }

        public IDisposable BloquearOperaciones() => Operaciones.Bloquear();

        private sealed class Ambito(ContextoCierre? anterior) : IDisposable
        {
            public void Dispose() => _actual.Value = anterior;
        }
    }
}
