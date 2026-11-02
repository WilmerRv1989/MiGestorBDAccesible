namespace MiGestorBDAccesible.Core.Trabajo
{
    public class ConjuntoPestanasModelo
    {
        private int _siguienteNumero = 1;

        public List<PestanaModelo> Pestanas { get; } = [];

        public PestanaModelo? Activa { get; private set; }

        public int Posicion(PestanaModelo pestana) => Pestanas.IndexOf(pestana) + 1;

        public PestanaModelo Abrir(string? baseDatos, string texto = "", string? archivo = null, string? titulo = null)
        {
            var pestana = new PestanaModelo
            {
                Numero = _siguienteNumero++,
                Titulo = titulo,
                BaseDatos = baseDatos,
                Texto = texto,
                Archivo = archivo,
                TextoGuardado = archivo is null ? "" : texto
            };

            var indice = Activa is null ? Pestanas.Count : Pestanas.IndexOf(Activa) + 1;
            Pestanas.Insert(indice, pestana);
            Activa = pestana;
            return pestana;
        }

        public void Activar(PestanaModelo pestana)
        {
            if (Pestanas.Contains(pestana))
            {
                Activa = pestana;
            }
        }

        public PestanaModelo? Cerrar(PestanaModelo pestana)
        {
            var indice = Pestanas.IndexOf(pestana);

            if (indice < 0)
            {
                return Activa;
            }

            Pestanas.RemoveAt(indice);

            if (Activa == pestana)
            {
                Activa = Pestanas.Count == 0 ? null : Pestanas[Math.Min(indice, Pestanas.Count - 1)];
            }

            if (Pestanas.Count == 0)
            {
                _siguienteNumero = 1;
            }

            return Activa;
        }

        public PestanaModelo? Mover(int desplazamiento)
        {
            if (Activa is null || Pestanas.Count == 0)
            {
                return null;
            }

            var indice = (Pestanas.IndexOf(Activa) + desplazamiento % Pestanas.Count + Pestanas.Count) % Pestanas.Count;
            Activa = Pestanas[indice];
            return Activa;
        }

        public SesionEditorModelo Guardar(Guid conexionId) => new()
        {
            ConexionId = conexionId,
            Activa = Activa is null ? 0 : Pestanas.IndexOf(Activa),
            Pestanas = Pestanas.Select(p => new PestanaGuardadaModelo
            {
                Numero = p.Numero,
                Titulo = p.Titulo,
                BaseDatos = p.BaseDatos,
                Texto = p.Texto,
                Archivo = p.Archivo,
                TextoGuardado = p.TextoGuardado
            }).ToList()
        };

        public static ConjuntoPestanasModelo Recuperar(SesionEditorModelo? sesion)
        {
            var conjunto = new ConjuntoPestanasModelo();

            if (sesion is null)
            {
                return conjunto;
            }

            var guardadas = sesion.Pestanas;

            if (guardadas.Count == 0 && (!string.IsNullOrWhiteSpace(sesion.Texto) || sesion.Archivo is not null))
            {
                guardadas = [new PestanaGuardadaModelo
                {
                    Numero = 1,
                    BaseDatos = sesion.BaseDatos,
                    Texto = sesion.Texto,
                    Archivo = sesion.Archivo,
                    TextoGuardado = sesion.TextoGuardado
                }];
            }

            foreach (var guardada in guardadas)
            {
                conjunto.Pestanas.Add(new PestanaModelo
                {
                    Numero = guardada.Numero,
                    Titulo = guardada.Titulo,
                    BaseDatos = guardada.BaseDatos,
                    Texto = guardada.Texto,
                    Archivo = guardada.Archivo,
                    TextoGuardado = guardada.TextoGuardado
                });
            }

            if (conjunto.Pestanas.Count > 0)
            {
                conjunto.Activa = conjunto.Pestanas[Math.Clamp(sesion.Activa, 0, conjunto.Pestanas.Count - 1)];
                conjunto._siguienteNumero = conjunto.Pestanas.Max(p => p.Numero) + 1;
            }

            return conjunto;
        }
    }
}
