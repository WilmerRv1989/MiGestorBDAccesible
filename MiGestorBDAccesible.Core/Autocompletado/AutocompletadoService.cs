using MiGestorBDAccesible.Core.Ayuda;
using MiGestorBDAccesible.Core.Conexiones;
using MiGestorBDAccesible.Core.Estructura;
using MiGestorBDAccesible.Core.Proveedores;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MiGestorBDAccesible.Core.Autocompletado
{
    public record SugerenciaModelo(string Texto, string Tipo, string Ayuda, bool AjustarMayusculas = false);

    public class VocabularioModelo
    {
        public List<string> Palabras { get; init; } = [];

        public List<string> Funciones { get; init; } = [];

        public List<string> Tipos { get; init; } = [];
    }

    public record ResultadoSugerenciasModelo(int Inicio, string Prefijo, List<SugerenciaModelo> Sugerencias);

    public partial class AutocompletadoService(AyudaService ayuda)
    {
        public const int Maximo = 50;

        private static readonly HashSet<string> _noSonAlias = new(StringComparer.OrdinalIgnoreCase)
        {
            "where", "on", "join", "inner", "left", "right", "full", "cross", "outer", "natural", "group", "order", "set", "values",
            "limit", "using", "as", "select", "union", "having", "with", "fetch", "offset", "returning", "window", "partition",
            "default", "intersect", "except", "minus", "into", "from", "for", "lateral", "straight_join", "when", "then", "else", "end"
        };

        private static readonly HashSet<string> _antesDeTabla = new(StringComparer.OrdinalIgnoreCase) { "from", "join", "update", "into", "table" };

        private static readonly HashSet<string> _antesDeProcedimiento = new(StringComparer.OrdinalIgnoreCase) { "call", "exec", "execute" };

        [GeneratedRegex("""(?:\b(?:FROM|JOIN|UPDATE|INTO)\b|,)\s*((?:[\w$#]+|"[^"]+"|\[[^\]]+\]|`[^`]+`)(?:\s*\.\s*(?:[\w$#]+|"[^"]+"|\[[^\]]+\]|`[^`]+`))?)(?:\s+(?:AS\s+)?([A-Za-z_][\w$#]*))?""", RegexOptions.IgnoreCase)]
        private static partial Regex TablaConAlias();

        [GeneratedRegex(@"\b(CREATE|ALTER|DROP|RENAME)\b", RegexOptions.IgnoreCase)]
        private static partial Regex CambioDeEstructura();

        [GeneratedRegex("""([A-Za-z_][\w$#]*)\s*$""")]
        private static partial Regex PalabraAnterior();

        private readonly Dictionary<TipoMotor, List<SugerenciaModelo>> _palabras = [];
        private readonly Dictionary<(ConexionActiva, string), Task<CatalogoModelo?>> _catalogos = [];
        private readonly Lock _cerrojo = new();

        public ResultadoSugerenciasModelo Sugerir(TipoMotor motor, string texto, int cursor) => Sugerir(motor, null, texto, cursor);

        public async Task<ResultadoSugerenciasModelo> SugerirAsync(IProveedorBaseDatos proveedor, ConexionActiva conexion, string? baseDatos, string texto, int cursor)
        {
            var catalogo = await CatalogoAsync(proveedor, conexion, baseDatos);
            return Sugerir(proveedor.Motor, catalogo, texto, cursor);
        }

        public static bool CambiaEstructura(string script) => CambioDeEstructura().IsMatch(script);

        public void Invalidar(ConexionActiva conexion)
        {
            var principal = conexion.Principal ?? conexion;

            lock (_cerrojo)
            {
                foreach (var clave in _catalogos.Keys.Where(k => ReferenceEquals(k.Item1, principal)).ToList())
                {
                    _catalogos.Remove(clave);
                }
            }
        }

        private Task<CatalogoModelo?> CatalogoAsync(IProveedorBaseDatos proveedor, ConexionActiva conexion, string? baseDatos)
        {
            var clave = (conexion.Principal ?? conexion, (baseDatos ?? "").ToUpperInvariant());

            lock (_cerrojo)
            {
                if (!_catalogos.TryGetValue(clave, out var tarea) || tarea.IsFaulted || (tarea.IsCompleted && tarea.Result is null))
                {
                    tarea = LeerCatalogoAsync(proveedor, conexion, baseDatos);
                    _catalogos[clave] = tarea;
                }

                return tarea;
            }
        }

        private static async Task<CatalogoModelo?> LeerCatalogoAsync(IProveedorBaseDatos proveedor, ConexionActiva conexion, string? baseDatos)
        {
            try
            {
                return await proveedor.ObtenerCatalogoAsync(conexion.Principal ?? conexion, baseDatos);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public ResultadoSugerenciasModelo Sugerir(TipoMotor motor, CatalogoModelo? catalogo, string texto, int cursor)
        {
            cursor = Math.Clamp(cursor, 0, texto.Length);
            var inicio = cursor;

            while (inicio > 0 && EsLetra(texto[inicio - 1]))
            {
                inicio--;
            }

            var prefijo = texto[inicio..cursor];
            var (desde, hasta) = Sentencia(texto, cursor);
            var antes = texto[desde..inicio];

            if (DentroDeTextoOComentario(antes))
            {
                return new ResultadoSugerenciasModelo(inicio, prefijo, []);
            }

            var candidatas = Candidatas(motor, catalogo, texto[desde..hasta], antes);
            var sugerencias = candidatas
                .Where(s => s.Texto.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase) || NombreSinEsquema(s.Texto).StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(s => (s.Texto, s.Tipo))
                .Take(Maximo)
                .ToList();

            return new ResultadoSugerenciasModelo(inicio, prefijo, sugerencias);
        }

        private IEnumerable<SugerenciaModelo> Candidatas(TipoMotor motor, CatalogoModelo? catalogo, string sentencia, string antes)
        {
            var objetos = catalogo?.Objetos ?? [];

            if (antes.EndsWith('.'))
            {
                var calificador = Limpiar(PalabraAnteriorConComillas(antes[..^1]));
                var tablas = Alias(sentencia, objetos);

                if (tablas.TryGetValue(calificador, out var conAlias))
                {
                    return Columnas(conAlias);
                }

                var porNombre = objetos.FirstOrDefault(o => o.Tipo is TipoObjetoCatalogo.Tabla or TipoObjetoCatalogo.Vista && o.Nombre.Equals(calificador, StringComparison.OrdinalIgnoreCase));

                if (porNombre is not null)
                {
                    return Columnas(porNombre);
                }

                return objetos
                    .Where(o => o.Esquema.Equals(calificador, StringComparison.OrdinalIgnoreCase))
                    .Select(o => Objeto(o, null));
            }

            var anterior = PalabraAnterior().Match(antes) is { Success: true } m ? m.Groups[1].Value : "";

            if (_antesDeTabla.Contains(anterior))
            {
                return objetos.Where(o => o.Tipo is TipoObjetoCatalogo.Tabla or TipoObjetoCatalogo.Vista)
                    .OrderBy(o => o.Nombre, StringComparer.OrdinalIgnoreCase)
                    .Select(o => Objeto(o, catalogo));
            }

            if (_antesDeProcedimiento.Contains(anterior))
            {
                return objetos.Where(o => o.Tipo == TipoObjetoCatalogo.Procedimiento)
                    .OrderBy(o => o.Nombre, StringComparer.OrdinalIgnoreCase)
                    .Select(o => Objeto(o, catalogo));
            }

            var columnas = Alias(sentencia, objetos).Values.Distinct().SelectMany(Columnas);
            var funciones = objetos.Where(o => o.Tipo == TipoObjetoCatalogo.Funcion).OrderBy(o => o.Nombre, StringComparer.OrdinalIgnoreCase).Select(o => Objeto(o, catalogo));

            return columnas.Concat(funciones).Concat(Palabras(motor));
        }

        private static Dictionary<string, ObjetoCatalogoModelo> Alias(string sentencia, List<ObjetoCatalogoModelo> objetos)
        {
            var resultado = new Dictionary<string, ObjetoCatalogoModelo>(StringComparer.OrdinalIgnoreCase);

            foreach (Match coincidencia in TablaConAlias().Matches(sentencia))
            {
                var partes = coincidencia.Groups[1].Value.Split('.').Select(p => Limpiar(p.Trim())).ToArray();
                var nombre = partes[^1];
                var esquema = partes.Length > 1 ? partes[0] : null;
                var objeto = objetos.FirstOrDefault(o => o.Tipo is TipoObjetoCatalogo.Tabla or TipoObjetoCatalogo.Vista
                    && o.Nombre.Equals(nombre, StringComparison.OrdinalIgnoreCase)
                    && (esquema is null || o.Esquema.Equals(esquema, StringComparison.OrdinalIgnoreCase)));

                if (objeto is null)
                {
                    continue;
                }

                resultado.TryAdd(nombre, objeto);

                if (coincidencia.Groups[2].Success && !_noSonAlias.Contains(coincidencia.Groups[2].Value))
                {
                    resultado[coincidencia.Groups[2].Value] = objeto;
                }
            }

            return resultado;
        }

        private static IEnumerable<SugerenciaModelo> Columnas(ObjetoCatalogoModelo objeto) =>
            objeto.Columnas.Select(c => new SugerenciaModelo(c.Nombre, $"columna de {objeto.Nombre}, {c.Tipo}", $"tipo:{c.Tipo}"));

        private static SugerenciaModelo Objeto(ObjetoCatalogoModelo objeto, CatalogoModelo? catalogo)
        {
            var calificado = catalogo is not null && objeto.Esquema.Length > 0 && !objeto.Esquema.Equals(catalogo.EsquemaPredeterminado, StringComparison.OrdinalIgnoreCase);
            var texto = calificado ? $"{objeto.Esquema}.{objeto.Nombre}" : objeto.Nombre;

            return objeto.Tipo switch
            {
                TipoObjetoCatalogo.Tabla => new SugerenciaModelo(texto, "tabla", "concepto:tabla"),
                TipoObjetoCatalogo.Vista => new SugerenciaModelo(texto, "vista", "concepto:vistas"),
                TipoObjetoCatalogo.Procedimiento => new SugerenciaModelo(texto, "procedimiento", string.Join('|', TipoObjeto.Procedimiento.TemasAyuda())),
                _ => new SugerenciaModelo(texto, "función de la base", string.Join('|', TipoObjeto.Funcion.TemasAyuda()))
            };
        }

        private static (int Desde, int Hasta) Sentencia(string texto, int cursor)
        {
            var desde = texto.LastIndexOf(';', Math.Max(cursor - 1, 0));
            var hasta = texto.IndexOf(';', cursor);
            return (desde < 0 || cursor == 0 ? 0 : desde + 1, hasta < 0 ? texto.Length : hasta);
        }

        private static bool DentroDeTextoOComentario(string antes)
        {
            var linea = antes[(antes.LastIndexOf('\n') + 1)..];
            return antes.Count(c => c == '\'') % 2 == 1 || linea.Contains("--", StringComparison.Ordinal);
        }

        private static string PalabraAnteriorConComillas(string texto)
        {
            var recortado = texto.TrimEnd();

            if (recortado.Length > 0 && recortado[^1] is '"' or ']' or '`')
            {
                var apertura = recortado[^1] == ']' ? '[' : recortado[^1];
                var inicio = recortado.LastIndexOf(apertura, recortado.Length - 2);
                return inicio < 0 ? "" : recortado[inicio..];
            }

            var fin = recortado.Length;
            var principio = fin;

            while (principio > 0 && EsLetra(recortado[principio - 1]))
            {
                principio--;
            }

            return recortado[principio..fin];
        }

        private static string Limpiar(string nombre) => nombre.Trim('"', '[', ']', '`');

        private static string NombreSinEsquema(string texto) => texto[(texto.LastIndexOf('.') + 1)..];

        public static bool EsLetra(char c) => char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '@';

        private List<SugerenciaModelo> Palabras(TipoMotor motor)
        {
            lock (_cerrojo)
            {
                if (_palabras.TryGetValue(motor, out var lista))
                {
                    return lista;
                }

                var vocabulario = Vocabulario(motor);

                lista = vocabulario.Palabras.Select(p => Sugerencia(motor, p, TipoPalabra, AyudaService.CategoriaPalabra))
                    .Concat(vocabulario.Funciones.Select(f => Sugerencia(motor, f, TipoFuncion, AyudaService.CategoriaPalabra)))
                    .Concat(vocabulario.Tipos.Select(t => Sugerencia(motor, t, TipoDato, AyudaService.CategoriaTipo)))
                    .DistinctBy(s => (s.Texto, s.Tipo))
                    .OrderBy(s => s.Texto, StringComparer.Ordinal)
                    .ToList();

                _palabras[motor] = lista;
                return lista;
            }
        }

        public const string TipoPalabra = "palabra de SQL";
        public const string TipoFuncion = "función";
        public const string TipoDato = "tipo de dato";

        private static readonly JsonSerializerOptions _opcionesJson = new() { PropertyNameCaseInsensitive = true };

        public static VocabularioModelo Vocabulario(TipoMotor motor)
        {
            using var flujo = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream($"MiGestorBDAccesible.Core.Autocompletado.Vocabulario.{motor}.json");

            return flujo is null
                ? new VocabularioModelo()
                : JsonSerializer.Deserialize<VocabularioModelo>(flujo, _opcionesJson) ?? new VocabularioModelo();
        }

        private SugerenciaModelo Sugerencia(TipoMotor motor, string texto, string tipo, string categoria)
        {
            var clave = $"{categoria}:{texto.ToLowerInvariant()}";
            var ficha = ayuda.Resolver(motor, clave);
            return new SugerenciaModelo(texto, tipo, ficha?.Id ?? $"{AyudaService.CategoriaPalabra}:{texto.ToLowerInvariant()}", true);
        }
    }
}
