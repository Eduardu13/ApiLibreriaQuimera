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
            cmd.Parameters.AddWithValue("@Contrasena", request.Contrasena); // Idealmente debería estar hasheada
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

// --- 2. INICIAR LA APLICACIÓN ---
app.Run();

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