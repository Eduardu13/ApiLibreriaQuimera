using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar CORS para permitir peticiones desde tu frontend (HTML/JS)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("AllowAll");

string connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=LibreriaQuimera;Integrated Security=True;TrustServerCertificate=True;";

// ==========================================
// ENDPOINTS DE LA API (Minimal API)
// ==========================================

app.MapGet("/obtener-productos", () =>
{
    var libros = new List<object>();
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            SELECT l.id_libro, l.titulo, l.precio, l.stock, l.autor, l.imagen, c.nombre AS categoria 
            FROM Libros l 
            INNER JOIN Categorias c ON l.id_categoria = c.id_categoria;";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    libros.Add(new
                    {
                        id = reader["id_libro"].ToString(),
                        titulo = reader["titulo"].ToString(),
                        precio = Convert.ToDecimal(reader["precio"]),
                        stock = Convert.ToInt32(reader["stock"]),
                        autor = reader["autor"].ToString(),
                        imagen = reader["imagen"].ToString(),
                        categoria = reader["categoria"].ToString()
                    });
                }
            }
        }
    }
    return Results.Ok(libros);
});

// --- RUTA: Registro de Usuarios ---
app.MapPost("/api/registro", (UsuarioRegistro request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "INSERT INTO Usuarios (nombre, apellido, correo, Contrasena, telefono) VALUES (@Nombre, @Apellido, @Correo, @Contrasena, @Telefono)";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Nombre", request.Nombre);
            cmd.Parameters.AddWithValue("@Apellido", request.Apellido);
            cmd.Parameters.AddWithValue("@Correo", request.Correo);
            cmd.Parameters.AddWithValue("@Contrasena", request.Contrasena);
            cmd.Parameters.AddWithValue("@Telefono", request.Telefono ?? (object)DBNull.Value);

            try
            {
                cmd.ExecuteNonQuery();
                return Results.Ok(new { mensaje = "Usuario registrado exitosamente" });
            }
            catch (SqlException)
            {
                return Results.BadRequest(new { mensaje = "El correo ya está registrado u ocurrió un error." });
            }
        }
    }
});

// --- RUTA: Login de Usuarios ---
app.MapPost("/api/login", (UsuarioLogin request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "SELECT id_usuario, nombre FROM Usuarios WHERE correo = @Correo AND Contrasena = @Contrasena";
        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Correo", request.Correo);
            cmd.Parameters.AddWithValue("@Contrasena", request.Contrasena);

            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (reader.Read())
                {
                    return Results.Ok(new
                    {
                        id = Convert.ToInt32(reader["id_usuario"]),
                        nombre = reader["nombre"].ToString()
                    });
                }
                else
                {
                    return Results.Unauthorized();
                }
            }
        }
    }
});

// --- RUTA: Procesar Compra y Actualizar Inventario ---
app.MapPost("/api/checkout", (CheckoutRequest request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        SqlTransaction transaction = conn.BeginTransaction();
        try
        {
            // 1. Insertar en Pedidos
            string queryPedido = "INSERT INTO Pedidos (id_usuario, total, direccion, ciudad, codigo_postal, telefono_envio) OUTPUT INSERTED.id_pedido VALUES (@IdUsuario, @Total, @Direccion, @Ciudad, @CP, @TelEnvio)";
            int idPedido = 0;
            using (SqlCommand cmdPedido = new SqlCommand(queryPedido, conn, transaction))
            {
                cmdPedido.Parameters.AddWithValue("@IdUsuario", request.IdUsuario);
                cmdPedido.Parameters.AddWithValue("@Total", request.Total);
                cmdPedido.Parameters.AddWithValue("@Direccion", request.Direccion);
                cmdPedido.Parameters.AddWithValue("@Ciudad", request.Ciudad);
                cmdPedido.Parameters.AddWithValue("@CP", request.CodigoPostal);
                cmdPedido.Parameters.AddWithValue("@TelEnvio", request.TelefonoEnvio);

                idPedido = (int)cmdPedido.ExecuteScalar();
            }

            // 2. Insertar Detalles del Pedido y actualizar el stock
            foreach (var item in request.Carrito)
            {
                string queryDetalle = "INSERT INTO Detalles_Pedido (id_pedido, id_libro, cantidad, precio_unitario) VALUES (@IdPedido, @IdLibro, @Cantidad, @Precio)";
                using (SqlCommand cmdDetalle = new SqlCommand(queryDetalle, conn, transaction))
                {
                    cmdDetalle.Parameters.AddWithValue("@IdPedido", idPedido);
                    cmdDetalle.Parameters.AddWithValue("@IdLibro", item.IdLibro);
                    cmdDetalle.Parameters.AddWithValue("@Cantidad", item.Cantidad);
                    cmdDetalle.Parameters.AddWithValue("@Precio", item.PrecioUnitario);
                    cmdDetalle.ExecuteNonQuery();
                }

                string queryStock = "UPDATE Libros SET stock = stock - @Cantidad WHERE id_libro = @IdLibro";
                using (SqlCommand cmdStock = new SqlCommand(queryStock, conn, transaction))
                {
                    cmdStock.Parameters.AddWithValue("@Cantidad", item.Cantidad);
                    cmdStock.Parameters.AddWithValue("@IdLibro", item.IdLibro);
                    cmdStock.ExecuteNonQuery();
                }
            }

            transaction.Commit();
            return Results.Ok(new { mensaje = "Compra procesada con éxito", idPedido = idPedido });
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            return Results.BadRequest(new { mensaje = "Error al procesar la compra", detalle = ex.Message });
        }
    }
});

// --- RUTA: Enviar Mensaje de Contacto ---
app.MapPost("/enviar-contacto", (ContactoRequest request) =>
{
    try
    {
        using (SqlConnection conn = new SqlConnection(connectionString))
        {
            conn.Open();
            string query = "INSERT INTO Mensajes_Contacto (nombre, telefono, correo, mensaje, fecha_envio) VALUES (@Nombre, @Telefono, @Correo, @Mensaje, GETDATE())";

            using (SqlCommand cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@Nombre", request.Nombre ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Telefono", request.Telefono ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Correo", request.Correo ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Mensaje", request.Mensaje ?? (object)DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
        return Results.Ok(new { exito = true, mensaje = "¡Mensaje enviado con éxito! Gracias por escribirnos." });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { exito = false, mensaje = "Error en el servidor: " + ex.Message });
    }
});

app.MapPost("/api/cambiar-password", async (CambioPasswordRequest request) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "UPDATE Usuarios SET Contrasena = @Contrasena WHERE correo = @Correo";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Contrasena", request.NuevaPassword);
            cmd.Parameters.AddWithValue("@Correo", request.Correo);

            int filasAfectadas = cmd.ExecuteNonQuery();

            if (filasAfectadas > 0)
            {
                return Results.Ok(new { mensaje = "¡Contraseña actualizada con éxito!" });
            }
            else
            {
                return Results.BadRequest(new { mensaje = "No se encontró el usuario." });
            }
        }
    }
});

string normalizarTexto(string texto)
{
    if (string.IsNullOrWhiteSpace(texto)) return "";

    string limpio = texto.Trim().ToLower();
    return limpio
        .Replace("á", "a")
        .Replace("é", "e")
        .Replace("í", "i")
        .Replace("ó", "o")
        .Replace("ú", "u");
}

string estimarLlegada(string ciudad)
{
    string t = normalizarTexto(ciudad);

    // 1. Local / Estado base (1 a 2 días hábiles)
    string[] zonaLocal = { "campeche", "ciudad del carmen" };
    if (zonaLocal.Any(x => t.Contains(x)))
        return "1 a 2 días hábiles";

    // 2. Sureste y península (2 a 4 días hábiles)
    string[] zonaSureste = {
        "yucatan", "merida", "valladolid", "tizimin",
        "quintana roo", "cancun", "playa del carmen", "chetumal", "tulum",
        "tabasco", "villahermosa", "cardenas",
        "chiapas", "tuxtla", "san cristobal", "tapachula",
        "oaxaca", "huatepec", "salina cruz",
        "veracruz", "xalapa", "coatza", "coatzacoalcos", "poza rica"
    };
    if (zonaSureste.Any(x => t.Contains(x)))
        return "2 a 4 días hábiles";

    // 3. Centro, Bajío y Occidente (3 a 5 días hábiles)
    string[] zonaCentro = {
        "ciudad de mexico", "cdmx", "df", "distrito federal",
        "estado de mexico", "edomex", "toluca", "ecatepec", "nezahualcoyotl", "naucalpan",
        "puebla", "tehuacan",
        "morelos", "cuernavaca",
        "tlaxcala",
        "hidalgo", "pachuca",
        "queretaro",
        "guanajuato", "leon", "irapuato", "celaya",
        "michoacan", "morelia", "uruapan",
        "jalisco", "guadalajara", "zapopan", "puerto vallarta",
        "colima", "manzanillo",
        "nayarit", "tepic",
        "guerrero", "acapulco", "chilpancingo",
        "san luis potosi",
        "aguascalientes",
        "zacatecas"
    };
    if (zonaCentro.Any(x => t.Contains(x)))
        return "3 a 5 días hábiles";

    // 4. Norte y Noroeste (4 a 6 días hábiles)
    string[] zonaNorte = {
        "nuevo leon", "monterrey", "san nicolas", "guadalupe", "apodaca",
        "coahuila", "saltillo", "torreon",
        "tamaulipas", "tampico", "reynosa", "matamoros", "ciudad victoria",
        "chihuahua", "juarez",
        "durango", "gomez palacio",
        "sinaloa", "culiacan", "mazatlan",
        "sonora", "hermosillo", "obregon", "nogales",
        "baja california sur", "la paz", "los cabos", "cabo san lucas",
        "baja california", "tijuana", "mexicali", "ensenada"
    };
    if (zonaNorte.Any(x => t.Contains(x)))
        return "4 a 6 días hábiles";

    return "4 a 6 días hábiles";
}

// --- RUTA: Consultar Compras del Usuario ---
app.MapGet("/mis-compras/{idUsuario}", (int idUsuario) =>
{
    var listaCompras = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select 
                p.id_pedido,
                p.total,
                p.direccion,
                p.ciudad,
                isnull(p.estado, 'Pendiente') as estado,
                stuff((
                    select ' | ' + l.titulo + ' (x' + cast(d.cantidad as varchar(10)) + ')'
                    from Detalles_Pedido d
                    inner join Libros l on d.id_libro = l.id_libro
                    where d.id_pedido = p.id_pedido
                    for xml path(''), type
                ).value('.', 'nvarchar(max)'), 1, 3, '') as libros
            from Pedidos p
            where p.id_usuario = @IdUsuario
            order by p.id_pedido desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string estadoActual = reader["estado"]?.ToString() ?? "Pendiente";
                    string direccionPedido = reader["direccion"] == DBNull.Value ? "Sin dirección" : reader["direccion"].ToString()!;
                    string ciudadPedido = reader["ciudad"] == DBNull.Value ? "Sin ciudad" : reader["ciudad"].ToString()!;
                    string librosTexto = reader["libros"] == DBNull.Value ? "Sin detalle" : reader["libros"].ToString()!;

                    listaCompras.Add(new
                    {
                        idPedido = reader["id_pedido"],
                        total = reader["total"],
                        direccion = direccionPedido,
                        ciudad = ciudadPedido,
                        estado = estadoActual,
                        libros = librosTexto,
                        estimadoLlegada = estadoActual == "Cancelado" ? "Pedido cancelado" : estimarLlegada(ciudadPedido)
                    });
                }
            }
        }
    }

    return Results.Ok(listaCompras);
});

app.MapGet("/mis-compras-correo/{correo}", (string correo) =>
{
    var listaCompras = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select 
                p.id_pedido,
                p.total,
                p.ciudad,
                isnull(p.estado, 'Pendiente') as estado,
                stuff((
                    select ', ' + l.titulo + ' (x' + cast(d.cantidad as varchar(10)) + ')'
                    from Detalles_Pedido d
                    inner join Libros l on d.id_libro = l.id_libro
                    where d.id_pedido = p.id_pedido
                    for xml path(''), type
                ).value('.', 'nvarchar(max)'), 1, 2, '') as libros
            from Pedidos p
            inner join Usuarios u on p.id_usuario = u.id_usuario
            where u.correo = @Correo
            order by p.id_pedido desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Correo", correo);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string estadoActual = reader["estado"]?.ToString() ?? "Pendiente";
                    string ciudadPedido = reader["ciudad"]?.ToString() ?? "";
                    string librosTexto = reader["libros"] == DBNull.Value ? "Sin detalle" : reader["libros"].ToString()!;

                    listaCompras.Add(new
                    {
                        idPedido = reader["id_pedido"],
                        total = reader["total"],
                        estado = estadoActual,
                        libros = librosTexto,
                        estimadoLlegada = estadoActual == "Cancelado" ? "Pedido cancelado" : estimarLlegada(ciudadPedido)
                    });
                }
            }
        }
    }

    return Results.Ok(listaCompras);
});

// --- RUTA: Cancelar Compra y Devolver Stock ---
app.MapPost("/cancelar-compra/{idPedido}", (int idPedido) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        SqlTransaction transaccion = conn.BeginTransaction();

        try
        {
            string queryStock = @"
                update Libros
                set stock = stock + d.cantidad
                from Libros l
                inner join Detalles_Pedido d on l.id_libro = d.id_libro
                where d.id_pedido = @IdPedido";

            using (SqlCommand cmdStock = new SqlCommand(queryStock, conn, transaccion))
            {
                cmdStock.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdStock.ExecuteNonQuery();
            }

            string queryEstado = "update Pedidos set estado = 'Cancelado' where id_pedido = @IdPedido";
            using (SqlCommand cmdEstado = new SqlCommand(queryEstado, conn, transaccion))
            {
                cmdEstado.Parameters.AddWithValue("@IdPedido", idPedido);
                cmdEstado.ExecuteNonQuery();
            }

            transaccion.Commit();
            return Results.Ok(new { exito = true, mensaje = "Compra cancelada y stock devuelto." });
        }
        catch (Exception ex)
        {
            transaccion.Rollback();
            return Results.Ok(new { exito = false, mensaje = "Error al cancelar compra: " + ex.Message });
        }
    }
});


// --- RUTA: Ver Comentarios Enviados por Correo ---
app.MapGet("/mis-comentarios/{correo}", (string correo) =>
{
    var listaComentarios = new List<object>();

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select id_mensaje, nombre, telefono, correo, mensaje, fecha_envio
            from Mensajes_Contacto
            where correo = @Correo
            order by fecha_envio desc, id_mensaje desc";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@Correo", correo);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    listaComentarios.Add(new
                    {
                        idMensaje = reader["id_mensaje"],
                        nombre = reader["nombre"]?.ToString() ?? "",
                        telefono = reader["telefono"]?.ToString() ?? "",
                        correo = reader["correo"]?.ToString() ?? "",
                        mensaje = reader["mensaje"]?.ToString() ?? "",
                        fechaEnvio = reader["fecha_envio"]?.ToString() ?? ""
                    });
                }
            }
        }
    }

    return Results.Ok(listaComentarios);
});

// --- RUTA: Eliminar Comentario de Contacto ---
app.MapDelete("/eliminar-comentario/{idMensaje}", (int idMensaje) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = "delete from Mensajes_Contacto where id_mensaje = @IdMensaje";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdMensaje", idMensaje);
            int filas = cmd.ExecuteNonQuery();

            if (filas > 0)
            {
                return Results.Ok(new { exito = true, mensaje = "Comentario eliminado." });
            }
            return Results.NotFound(new { exito = false, mensaje = "No se encontró el comentario." });
        }
    }
});





// --- RUTA: Obtener datos del perfil por ID ---
app.MapGet("/api/perfil/{idUsuario}", (int idUsuario) =>
{
    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();
        string query = @"
            select id_usuario, nombre, apellido, correo, telefono, direccion, ciudad, codigo_postal
            from Usuarios
            where id_usuario = @IdUsuario";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                if (!reader.Read())
                    return Results.NotFound(new { mensaje = "Usuario no encontrado." });

                return Results.Ok(new
                {
                    id = reader["id_usuario"],
                    nombre = reader["nombre"]?.ToString() ?? "",
                    apellido = reader["apellido"]?.ToString() ?? "",
                    correo = reader["correo"]?.ToString() ?? "",
                    telefono = reader["telefono"]?.ToString() ?? "",
                    direccion = reader["direccion"]?.ToString() ?? "",
                    ciudad = reader["ciudad"]?.ToString() ?? "",
                    codigo_postal = reader["codigo_postal"]?.ToString() ?? ""
                });
            }
        }
    }
});

// --- RUTA: Actualizar perfil (correo NO editable) ---
app.MapPut("/api/perfil/{idUsuario}", (int idUsuario, PerfilUpdateDto datos) =>
{
    if (string.IsNullOrWhiteSpace(datos.Nombre))
        return Results.BadRequest(new { mensaje = "El nombre es obligatorio." });

    using (SqlConnection conn = new SqlConnection(connectionString))
    {
        conn.Open();

        string query = string.IsNullOrWhiteSpace(datos.Contrasena)
            ? @"update Usuarios
                set nombre = @Nombre,
                    apellido = @Apellido,
                    telefono = @Telefono,
                    direccion = @Direccion,
                    ciudad = @Ciudad,
                    codigo_postal = @CodigoPostal
                where id_usuario = @IdUsuario"
            : @"update Usuarios
                set nombre = @Nombre,
                    apellido = @Apellido,
                    contrasena = @Contrasena,
                    telefono = @Telefono,
                    direccion = @Direccion,
                    ciudad = @Ciudad,
                    codigo_postal = @CodigoPostal
                where id_usuario = @IdUsuario";

        using (SqlCommand cmd = new SqlCommand(query, conn))
        {
            cmd.Parameters.AddWithValue("@IdUsuario", idUsuario);
            cmd.Parameters.AddWithValue("@Nombre", datos.Nombre.Trim());
            cmd.Parameters.AddWithValue("@Apellido", (datos.Apellido ?? "").Trim());
            cmd.Parameters.AddWithValue("@Telefono", (datos.Telefono ?? "").Trim());
            cmd.Parameters.AddWithValue("@Direccion", (datos.Direccion ?? "").Trim());
            cmd.Parameters.AddWithValue("@Ciudad", (datos.Ciudad ?? "").Trim());
            cmd.Parameters.AddWithValue("@CodigoPostal", (datos.CodigoPostal ?? "").Trim());

            if (!string.IsNullOrWhiteSpace(datos.Contrasena))
                cmd.Parameters.AddWithValue("@Contrasena", datos.Contrasena);

            int filas = cmd.ExecuteNonQuery();
            if (filas == 0)
                return Results.NotFound(new { mensaje = "No se pudo actualizar el perfil." });
        }
    }

    return Results.Ok(new { mensaje = "Perfil actualizado correctamente." });
});



// --- 2. INICIAR LA APLICACIÓN ---
app.Run();

record PerfilUpdateDto(
    string Nombre,
    string? Apellido,
    string? Contrasena,
    string? Telefono,
    string? Direccion,
    string? Ciudad,
    string? CodigoPostal
);

// --- 3. EL RECORD SIEMPRE VA HASTA ABAJO DE TODO EL ARCHIVO ---
public record CambioPasswordRequest(string Correo, string NuevaPassword);

// ==========================================
// CLASES AUXILIARES (Data Transfer Objects)
// ==========================================

public class UsuarioRegistro
{
    public string Nombre { get; set; }
    public string Apellido { get; set; }
    public string Correo { get; set; }
    public string Contrasena { get; set; }
    public string Telefono { get; set; }
}

public class UsuarioLogin
{
    public string Correo { get; set; }
    public string Contrasena { get; set; }
}

public class CheckoutRequest
{
    public int IdUsuario { get; set; }
    public decimal Total { get; set; }
    public string Direccion { get; set; }
    public string Ciudad { get; set; }
    public string CodigoPostal { get; set; }
    public string TelefonoEnvio { get; set; }
    public List<ItemCarrito> Carrito { get; set; }
}

public class ItemCarrito
{
    public string IdLibro { get; set; }
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
}

public class ContactoRequest
{
    public string Nombre { get; set; }
    public string Telefono { get; set; }
    public string Correo { get; set; }
    public string Mensaje { get; set; }
}