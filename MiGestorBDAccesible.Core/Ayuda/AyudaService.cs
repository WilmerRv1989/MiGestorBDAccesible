using MiGestorBDAccesible.Core.Conexiones;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MiGestorBDAccesible.Core.Ayuda
{
    public class AyudaService
    {
        public const string CategoriaTipo = "tipo";
        public const string CategoriaConcepto = "concepto";
        public const string CategoriaPalabra = "palabra";
        public const string CategoriaPlSql = "plsql";
        public const string CategoriaAtajos = "atajos";

        private static readonly JsonSerializerOptions _opcionesJson = new() { PropertyNameCaseInsensitive = true };

        private readonly Dictionary<TipoMotor, ContenidoAyudaModelo> _contenidos = [];
        private readonly Lock _cerrojo = new();

        public ContenidoAyudaModelo Contenido(TipoMotor motor)
        {
            lock (_cerrojo)
            {
                if (!_contenidos.TryGetValue(motor, out var contenido))
                {
                    contenido = Cargar(motor);
                    _contenidos[motor] = contenido;
                }

                return contenido;
            }
        }

        public FichaAyudaModelo? Obtener(TipoMotor motor, string id) =>
            Contenido(motor).Fichas.FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        public FichaAyudaModelo? Resolver(TipoMotor motor, string clave)
        {
            var directa = Obtener(motor, clave);
            var separador = clave.IndexOf(':');

            if (directa is not null || separador < 0)
            {
                return directa;
            }

            var categoria = clave[..separador];
            var valor = clave[(separador + 1)..];

            if (categoria == CategoriaTipo)
            {
                var parentesis = valor.IndexOf('(');
                valor = parentesis >= 0 ? valor[..parentesis] : valor;
            }

            valor = valor.Trim().ToLowerInvariant();
            var fichas = Contenido(motor).Fichas;

            var porCategoria = fichas.FirstOrDefault(f =>
                f.Categoria == categoria
                && (f.Id.Equals($"{categoria}:{valor}", StringComparison.OrdinalIgnoreCase)
                    || f.Alias.Contains(valor, StringComparer.OrdinalIgnoreCase)));

            if (porCategoria is not null || categoria != CategoriaPalabra)
            {
                return porCategoria;
            }

            return fichas.FirstOrDefault(f => f.Alias.Contains(valor, StringComparer.OrdinalIgnoreCase));
        }

        public List<FichaAyudaModelo> ResolverVarias(TipoMotor motor, IEnumerable<string> claves) =>
            claves.Select(c => Resolver(motor, c))
                .OfType<FichaAyudaModelo>()
                .DistinctBy(f => f.Id)
                .ToList();

        public List<FichaAyudaModelo> Buscar(TipoMotor motor, string texto)
        {
            var buscado = Normalizar(texto);

            if (buscado.Length == 0)
            {
                return [];
            }

            return Contenido(motor).Fichas
                .Select(f => (Ficha: f, Puntos: Puntuar(f, buscado)))
                .Where(r => r.Puntos > 0)
                .OrderByDescending(r => r.Puntos)
                .ThenBy(r => r.Ficha.Titulo, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => r.Ficha)
                .ToList();
        }

        private static int Puntuar(FichaAyudaModelo ficha, string buscado)
        {
            if (ficha.Alias.Any(a => Normalizar(a) == buscado))
            {
                return 4;
            }

            if (Normalizar(ficha.Titulo).Contains(buscado))
            {
                return 3;
            }

            if (ficha.Alias.Any(a => Normalizar(a).Contains(buscado)))
            {
                return 2;
            }

            return Normalizar(ficha.Resumen).Contains(buscado) ? 1 : 0;
        }

        private static string Normalizar(string texto)
        {
            var descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var resultado = new StringBuilder(descompuesto.Length);

            foreach (var caracter in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark)
                {
                    resultado.Append(caracter);
                }
            }

            return resultado.ToString();
        }

        private static ContenidoAyudaModelo Cargar(TipoMotor motor)
        {
            var ensamblado = Assembly.GetExecutingAssembly();
            var recurso = $"MiGestorBDAccesible.Core.Ayuda.{motor}.fichas.json";

            using var flujo = ensamblado.GetManifestResourceStream(recurso);

            if (flujo is null)
            {
                return new ContenidoAyudaModelo { Motor = motor.Nombre() };
            }

            return JsonSerializer.Deserialize<ContenidoAyudaModelo>(flujo, _opcionesJson)
                ?? new ContenidoAyudaModelo { Motor = motor.Nombre() };
        }
    }
}
