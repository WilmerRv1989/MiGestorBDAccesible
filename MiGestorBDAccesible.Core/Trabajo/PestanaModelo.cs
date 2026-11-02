using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Ejecucion;

namespace MiGestorBDAccesible.Core.Trabajo
{
    public class PestanaModelo
    {
        public Guid Id { get; } = Guid.NewGuid();

        public int Numero { get; init; }

        public string? Titulo { get; set; }

        public string? BaseDatos { get; set; }

        public string Texto { get; set; } = "";

        public string? Archivo { get; set; }

        public string TextoGuardado { get; set; } = "";

        public ResultadoEjecucionModelo? Resultado { get; set; }

        public bool Ejecutando { get; set; }

        public CancellationTokenSource? Cancelacion { get; set; }

        public bool CambiosPendientes { get; set; }

        public ConexionActiva? Sesion { get; set; }

        public string Nombre => Archivo is not null ? Path.GetFileName(Archivo) : Titulo ?? $"Consulta {Numero}";

        public bool Modificado => Texto != TextoGuardado;

        public bool EstaVacia => Archivo is null && string.IsNullOrWhiteSpace(Texto);

        public string Etiqueta
        {
            get
            {
                var partes = new List<string> { Nombre, BaseDatos ?? "base predeterminada" };

                if (Archivo is not null && Modificado)
                {
                    partes.Add("con cambios sin guardar");
                }

                if (CambiosPendientes)
                {
                    partes.Add("con cambios sin confirmar");
                }

                if (Ejecutando)
                {
                    partes.Add("ejecutando");
                }

                return string.Join(", ", partes);
            }
        }
    }
}
