using Markdig;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace MiGestorBDAccesible.Core.Actualizaciones
{
    public record InstaladorPublicadoModelo(string Nombre, string Url, long Tamano, string? Sha256);

    public class VersionPublicadaModelo
    {
        public Version Version { get; init; } = new(0, 0);

        public string Etiqueta { get; init; } = "";

        public string Titulo { get; init; } = "";

        public string Notas { get; init; } = "";

        public string Pagina { get; init; } = "";

        public InstaladorPublicadoModelo? Instalador { get; init; }

        public string VersionCorta => InformacionAplicacion.VersionCorta(Version.ToString(3));
    }

    public class HuellaIncorrectaException(string mensaje) : Exception(mensaje);

    public class ActualizacionesService
    {
        public const string DireccionUltimaVersion = "https://api.github.com/repos/WilmerRv1989/MiGestorBDAccesible/releases/latest";

        private readonly HttpClient _http;

        public ActualizacionesService(HttpClient http)
        {
            _http = http;
        }

        public static HttpClient CrearCliente(string versionActual)
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MiGestorBDAccesible", versionActual));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return http;
        }

        public async Task<VersionPublicadaModelo> ConsultarAsync(CancellationToken cancelacion = default)
        {
            using var respuesta = await _http.GetAsync(DireccionUltimaVersion, cancelacion);
            respuesta.EnsureSuccessStatusCode();
            var json = await respuesta.Content.ReadAsStringAsync(cancelacion);
            return Leer(json);
        }

        public static VersionPublicadaModelo Leer(string json)
        {
            using var documento = JsonDocument.Parse(json);
            var raiz = documento.RootElement;
            var etiqueta = raiz.GetProperty("tag_name").GetString() ?? "";

            var instalador = raiz.TryGetProperty("assets", out var archivos)
                ? archivos.EnumerateArray()
                    .Select(a => new InstaladorPublicadoModelo(
                        a.GetProperty("name").GetString() ?? "",
                        a.GetProperty("browser_download_url").GetString() ?? "",
                        a.GetProperty("size").GetInt64(),
                        a.TryGetProperty("digest", out var huella) && huella.ValueKind == JsonValueKind.String ? huella.GetString() : null))
                    .FirstOrDefault(a => a.Nombre.EndsWith("-instalador.exe", StringComparison.OrdinalIgnoreCase))
                : null;

            return new VersionPublicadaModelo
            {
                Version = LeerVersion(etiqueta) ?? new Version(0, 0),
                Etiqueta = etiqueta,
                Titulo = raiz.TryGetProperty("name", out var nombre) ? nombre.GetString() ?? etiqueta : etiqueta,
                Notas = raiz.TryGetProperty("body", out var cuerpo) ? cuerpo.GetString() ?? "" : "",
                Pagina = raiz.TryGetProperty("html_url", out var pagina) ? pagina.GetString() ?? "" : "",
                Instalador = instalador
            };
        }

        public static Version? LeerVersion(string texto)
        {
            var limpio = texto.Trim().TrimStart('v', 'V').Split('-', '+')[0];
            return Version.TryParse(limpio.Contains('.') ? limpio : $"{limpio}.0", out var version) ? Normalizar(version) : null;
        }

        private static Version Normalizar(Version version) =>
            new(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

        public static bool EsMasNueva(VersionPublicadaModelo publicada, string versionActual) =>
            LeerVersion(versionActual) is { } actual && publicada.Version > actual;

        public static bool DebeBuscar(PreferenciasModelo preferencias, DateTimeOffset ahora) =>
            preferencias.BuscarAlIniciar && (preferencias.UltimaBusqueda is not { } ultima || ahora - ultima >= TimeSpan.FromDays(1));

        public static bool DebeAvisar(VersionPublicadaModelo publicada, string versionActual, PreferenciasModelo preferencias) =>
            EsMasNueva(publicada, versionActual) && preferencias.VersionOmitida != publicada.Etiqueta;

        public static string AnuncioAlIniciar(VersionPublicadaModelo publicada) =>
            $"Hay una versión nueva de {InformacionAplicacion.Nombre}. Ábrela con el menú Ayuda, Buscar actualizaciones. Versión {publicada.VersionCorta}.";

        public async Task<string> DescargarAsync(VersionPublicadaModelo publicada, string carpeta, IProgress<double>? avance = null, CancellationToken cancelacion = default)
        {
            var instalador = publicada.Instalador ?? throw new InvalidOperationException("La versión publicada no tiene instalador.");
            Directory.CreateDirectory(carpeta);
            var destino = Path.Combine(carpeta, Path.GetFileName(instalador.Nombre));
            var temporal = destino + ".descarga";

            if (File.Exists(destino) && await HuellaCorrectaAsync(destino, instalador.Sha256, cancelacion))
            {
                avance?.Report(1);
                return destino;
            }

            try
            {
                using (var respuesta = await _http.GetAsync(instalador.Url, HttpCompletionOption.ResponseHeadersRead, cancelacion))
                {
                    respuesta.EnsureSuccessStatusCode();
                    var total = respuesta.Content.Headers.ContentLength ?? instalador.Tamano;
                    await using var origen = await respuesta.Content.ReadAsStreamAsync(cancelacion);
                    await using var archivo = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None);
                    var bufer = new byte[81920];
                    long leidos = 0;
                    int cantidad;

                    while ((cantidad = await origen.ReadAsync(bufer, cancelacion)) > 0)
                    {
                        await archivo.WriteAsync(bufer.AsMemory(0, cantidad), cancelacion);
                        leidos += cantidad;

                        if (total > 0)
                        {
                            avance?.Report((double)leidos / total);
                        }
                    }
                }

                if (!await HuellaCorrectaAsync(temporal, instalador.Sha256, cancelacion))
                {
                    throw new HuellaIncorrectaException("El archivo descargado no coincide con el publicado. Por seguridad no se instaló.");
                }

                File.Move(temporal, destino, overwrite: true);
                return destino;
            }
            finally
            {
                if (File.Exists(temporal))
                {
                    File.Delete(temporal);
                }
            }
        }

        public static string NotasHtml(string notas)
        {
            var tuberia = new Markdig.MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();
            var documento = Markdig.Markdown.Parse(notas, tuberia);

            foreach (var encabezado in Markdig.Syntax.MarkdownObjectExtensions.Descendants<Markdig.Syntax.HeadingBlock>(documento))
            {
                encabezado.Level = Math.Min(encabezado.Level + 2, 6);
            }

            return Markdig.Markdown.ToHtml(documento, tuberia);
        }

        public static async Task<bool> HuellaCorrectaAsync(string ruta, string? huellaPublicada, CancellationToken cancelacion = default)
        {
            if (huellaPublicada is null || !huellaPublicada.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            await using var archivo = File.OpenRead(ruta);
            var calculada = Convert.ToHexString(await SHA256.HashDataAsync(archivo, cancelacion));
            return string.Equals(calculada, huellaPublicada["sha256:".Length..], StringComparison.OrdinalIgnoreCase);
        }
    }
}
