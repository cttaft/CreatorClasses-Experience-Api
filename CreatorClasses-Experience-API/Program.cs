using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
builder.Services.AddCors(options => options.AddPolicy("allowAny", o => o.AllowAnyOrigin()));
builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetValue<string>("CosmosConnectionString" );
builder.Services.AddCosmos<ClassesDb>(connectionString, "classes");

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();



app.MapGet("/classes", async (ClassesDb db) =>
{
    var classesFromDb = await db.Classes.ToListAsync();
    var classResponse = new List<CreatorClassTest>();
    foreach (var cl in classesFromDb)
    {
        var videos = await db.Videos.Where(a => a.Id == cl.Id).ToListAsync();
        classResponse.Add(new CreatorClassTest(int.Parse(cl.Id), cl.Name, cl.ImageSrc, cl.Description,
            videos.Select(v => new VideoTest(int.Parse(v.VideoId), v.Name, v.Description, v.VideoSrc, v.Seconds)).ToArray(),
            cl.CreatorId));

    }
    return classResponse;
})
.WithName("GetClasses");



app.MapGet("/classes/{id}", async (ClassesDb db, string id) =>
{
    var classFromDb = await db.Classes.SingleAsync(a => a.Id == id);
    
    return classFromDb;
})
.WithName("GetClass");


app.MapGet("/subscriptions", async (ClassesDb db, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });

    var classesFromDb = await db.Classes.ToListAsync();
    var classResponse = new List<CreatorClassTest>();
    foreach (var cl in classesFromDb)
    {
        var videos = await db.Videos.Where(a => a.Id == cl.Id).ToListAsync();
        classResponse.Add(new CreatorClassTest(int.Parse(cl.Id), cl.Name, cl.ImageSrc, cl.Description,
            videos.Select(v => new VideoTest(int.Parse(v.VideoId), v.Name, v.Description, v.VideoSrc, v.Seconds)).ToArray(),
            cl.CreatorId));

    }
    return classResponse;
})
.WithName("GetSubscriptions").RequireAuthorization();

app.Run();

record CreatorClassTest(int classId, string className, string classImage, string classDescription, VideoTest[] videos, int creatorId)
{
    public int ClassId = classId;
    public string ClassName = className;
    public string ClassImage = classImage;
    public string ClassDescription = classDescription;
    public VideoTest[] Videos = videos;
    public int CratorId = creatorId;
}

public class CreatorClass
{
    public CreatorClass()
    {

    }
    public string Id { get; set; }
    public string Name { get; set; }
    public string ImageSrc { get; set; }
    public string Description { get; set; }
    public int CreatorId { get; set; }
}

public class Video
{
    public Video()
    {

    }
    public string Id { get; set; }
    public string VideoId { get; set; }
    public string Name { get; set; }
    public string VideoSrc { get; set; }
    public string Description { get; set; }
    public int CreatorId { get; set; }

    public int Seconds { get; set; }
}

record VideoTest(int videoId, string title, string description, string videoSrc, int seconds)
{
    public int VideoId = videoId;
    public string Title = title;
    public string Description = description;
    public string VideoSrc = videoSrc;
    public int Seconds = seconds;
}

class ClassesDb : DbContext
{
    public ClassesDb(DbContextOptions options) : base(options) { }
    public DbSet<CreatorClass> Classes { get; set; }

    public DbSet<Video> Videos { get; set; }


}

