using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Azure.Identity;
using Azure.Storage.Blobs;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Azure.Messaging.ServiceBus;


var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
builder.Services.AddCors(options => options.AddPolicy("allowAny", o =>
 {
     o.AllowAnyOrigin();
     o.AllowCredentials();
 }));
builder.Services.AddAuthorization();

var cosmosConnectionString = builder.Configuration.GetValue<string>("CosmosConnectionString");
builder.Services.AddCosmos<ClassesDb>(cosmosConnectionString, "classes");

var sbConnectionString = builder.Configuration.GetValue<string>("SBConnectionString");

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
    var classResponse = new List<CreatorClassDto>();
    foreach (var cl in classesFromDb)
    {
        classResponse.Add(new CreatorClassDto
    (int.Parse(cl.Id), cl.Name, cl.ImageSrc, cl.Description,
            cl.Videos?.Select(v => new VideoDto(int.Parse(v.VideoId), v.Name, v.Description, v.VideoSrc, v.Seconds)).ToArray(),
            cl.CreatorId));

    }
    return classResponse;
})
.WithName("GetClasses");

app.MapPost("/classes", async (ClassesDb db, HttpContext context, [FromBody] CreatorClassDto creatorClassDto) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();

    var cl = await db.Classes.FirstOrDefaultAsync(cr => cr.Id == creatorClassDto.classId.ToString());
    int id = creatorClassDto.classId;
    if (cl == null)
    {

        var highestClassId = await db.Classes.MaxAsync(c => c.Id);
        id = int.Parse(highestClassId) + 1;
        db.Classes.Add(new CreatorClass
        {
            Id = id.ToString(),
            CreatorId = creatorClassDto.CreatorId,
            Description = creatorClassDto.ClassDescription,
            Name = creatorClassDto.ClassName
        });


    }
    else
    {

        cl.Description = creatorClassDto.ClassDescription;
        cl.Name = creatorClassDto.ClassName;
        db.Classes.Update(cl);
    }

    await db.SaveChangesAsync();

    return Results.Ok(new { Id = id });
})
.WithName("CreateClass");

app.MapGet("/classes/byCreator/{id}", async (string id, ClassesDb db) =>
{
    var validId = int.TryParse(id, out int creatorId);
    if (!validId)
    {
        return Results.BadRequest();
    }

    var classesFromDb = await db.Classes.Where(c => c.CreatorId == creatorId).ToListAsync();

    if (classesFromDb == null || !classesFromDb.Any())
    {
        return Results.NotFound();
    }
    var classResponse = new List<CreatorClassDto>();
    foreach (var cl in classesFromDb)
    {
        classResponse.Add(new CreatorClassDto
    (int.Parse(cl.Id), cl.Name, cl.ImageSrc, cl.Description,
            cl.Videos?.Select(v => new VideoDto(int.Parse(v.VideoId), v.Name, v.Description, v.VideoSrc, v.Seconds)).ToArray(),
            cl.CreatorId));

    }
    return Results.Ok(classResponse);

})
.WithName("GetClassesByCreator");



app.MapGet("/classes/{id}", async (ClassesDb db, string id) =>
{
    var classFromDb = await db.Classes.SingleAsync(a => a.Id == id);
    return new CreatorClassDto
(int.Parse(classFromDb.Id), classFromDb.Name, classFromDb.ImageSrc, classFromDb.Description,
         classFromDb.Videos?.Select(a => new VideoDto(int.Parse(a.VideoId), a.Name, a.Description, a.VideoSrc, a.Seconds)).ToArray(),
        classFromDb.CreatorId);
})
.WithName("GetClass");


app.MapPost("/classes/{id}/picture", async (ClassesDb db, HttpContext context, HttpRequest req, string id) =>
{
    //validate that they own the class...
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                            "creatorclassblobstorage",
                                            "classpictures");
    BlobContainerClient containerClient = new BlobContainerClient(new Uri(containerEndpoint),
            new DefaultAzureCredential());

    var currentTicks = DateTime.Now.Ticks;

    var blobClient = containerClient.GetBlobClient(id + currentTicks);

    using (var memoryStream = new MemoryStream())
    {
        using (var image = Image.Load(req.Form.Files.FirstOrDefault().OpenReadStream()))
        {

            await image.SaveAsJpegAsync(memoryStream);
        }
        memoryStream.Position = 0;
        await blobClient.UploadAsync(memoryStream, overwrite: true);
    }

    var classToUpdate = db.Classes.First(a => a.Id == id);
    classToUpdate.ImageSrc = $"https://creatorclassblobstorage.blob.core.windows.net/classpictures/{id}{currentTicks}";
    await db.SaveChangesAsync();


    return Results.Ok();
})
.WithName("SaveClassPicture").RequireAuthorization();

app.MapPost("/classes/{id}/videos", async (ClassesDb db, HttpContext context, int id, [FromBody] VideoDto video) =>
{

    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var classFromDb = db.Classes.FirstOrDefault(a => a.Id == id.ToString());
    if (classFromDb is null)
    {
        return Results.NotFound();
    }
    var vidId = 0;

    if(classFromDb.Videos != null && classFromDb.Videos.Any())
    {
         vidId = classFromDb.Videos.Max(a => int.Parse(a.VideoId));
    }
    else{
        classFromDb.Videos = new List<Video>();
    }

   
    classFromDb.Videos.Add(new Video
    {
        VideoId = (vidId + 1).ToString(),
        Name = video.Title,
        Description = video.Description,
        VideoSrc = video.VideoSrc,
        Seconds = video.Seconds
    });

   await db.SaveChangesAsync();

   //Send message..
    var client = new ServiceBusClient(sbConnectionString);
    var sender = client.CreateSender("new-video-q");

    await sender.SendMessageAsync(new ServiceBusMessage(id.ToString()));


   return Results.Ok();


}).WithName("SaveVideo").RequireAuthorization();


app.MapGet("/subscriptions", async (ClassesDb db, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();
    var classResponse = new List<CreatorClassDto>();


    var subs = await db.Subscriptions.Where(sub => sub.UserId == userId).ToListAsync();
    if (!subs.Any())
    {
        return classResponse;
    }
    var classesFromDb = await db.Classes.Where(c => subs.Select(sub => sub.ClassId).Contains(c.Id)).ToListAsync();
    foreach (var cl in classesFromDb)
    {
        classResponse.Add(new CreatorClassDto
    (int.Parse(cl.Id), cl.Name, cl.ImageSrc, cl.Description,
            cl.Videos?.Select(v => new VideoDto(int.Parse(v.VideoId), v.Name, v.Description, v.VideoSrc, v.Seconds)).ToArray(),
            cl.CreatorId));

    }
    return classResponse;
})
.WithName("GetSubscriptions").RequireAuthorization();

app.MapPost("/subscriptions", async ([FromBody] SubscriptionRequest subRequest, ClassesDb db, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();
    var emails = context.User.Claims.FirstOrDefault(c => c.Type == "emails").Value;

    db.Subscriptions.Add(new Subscription
    {
        Id = subRequest.ClassId.ToString() + ":" + userId,
        UserId = userId,
        ClassId = subRequest.ClassId.ToString(),
        EmailAddress = emails
    });

    await db.SaveChangesAsync();
    return subRequest.ClassId;
})
.WithName("Subscribe").RequireAuthorization();


app.MapDelete("/subscriptions/[id]", async (string id, ClassesDb db, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();

    var sub =db.Subscriptions.FirstOrDefault(a => a.ClassId == id);
    if(sub != null)
    {

        db.Subscriptions.Remove(sub);
        
        await db.SaveChangesAsync();
    }
    return Results.NoContent();
})
.WithName("DeleteSubscription").RequireAuthorization();


app.MapGet("/creatorProfile", async (ClassesDb db, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();


    var creator = await db.Creators.FirstOrDefaultAsync(cr => cr.UserId == userId);
    if (creator == null)
    {

        return Results.NotFound();
    }

    return Results.Ok(creator);
})
.WithName("GetCreatorProfile").RequireAuthorization();

app.MapPost("/creatorProfile", async (ClassesDb db, [FromBody] CreatorProfileDto profile, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();

    var creator = await db.Creators.FirstOrDefaultAsync(cr => cr.UserId == userId);
    if (creator == null)
    {

        var highestCreatorId = await db.Creators.MaxAsync(c => c.CreatorId);

        db.Creators.Add(new CreatorProfile
        {
            Id = (highestCreatorId + 1).ToString(),
            CreatorId = highestCreatorId + 1,
            Description = profile.Description,
            UserId = userId,
            Name = profile.Name,
            YoutubeUrl = profile.YoutubeUrl
        });


    }
    else
    {

        creator.Description = profile.Description;
        creator.Name = profile.Name;
        creator.YoutubeUrl = profile.YoutubeUrl;
        db.Creators.Update(creator);
    }

    await db.SaveChangesAsync();

    return Results.Ok();
})
.WithName("SaveCreatorProfile").RequireAuthorization();

app.MapPost("/creatorProfile/Picture", async (ClassesDb db, HttpRequest req, HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });
    var userId = context.User.GetObjectId();
    string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                            "creatorclassblobstorage",
                                            "profilepictures");
    BlobContainerClient containerClient = new BlobContainerClient(new Uri(containerEndpoint),
            new DefaultAzureCredential());

    var currentTicks = DateTime.Now.Ticks;

    var blobClient = containerClient.GetBlobClient(userId + currentTicks);

    using (var memoryStream = new MemoryStream())
    {
        using (var image = Image.Load(req.Form.Files.FirstOrDefault().OpenReadStream()))
        {
            image.Mutate(a => a.Resize(180, 180));
            await image.SaveAsJpegAsync(memoryStream);
        }
        memoryStream.Position = 0;
        await blobClient.UploadAsync(memoryStream, overwrite: true);
    }

    var creator = db.Creators.First(a => a.UserId == userId);
    creator.ImageSrc = $"https://creatorclassblobstorage.blob.core.windows.net/profilepictures/{userId}{currentTicks}";
    await db.SaveChangesAsync();


    return Results.Ok();
}).Accepts<string>("multipart/form-data")
.Produces(200).RequireAuthorization();



app.MapGet("/creators", async (ClassesDb db) =>
{
    var creators = await db.Creators.ToListAsync();
    return Results.Ok(creators);
})
.WithName("GetCreators");

app.MapGet("/creators/{id}", async (string id, ClassesDb db) =>
{
    var validId = int.TryParse(id, out int creatorId);
    if (!validId)
    {
        return Results.BadRequest();
    }
    var creator = await db.Creators.FirstOrDefaultAsync(a => a.CreatorId == creatorId);
    if (creator == null)
    {
        return Results.NotFound();
    }
    return Results.Ok(creator);
})
.WithName("GetCreator");

app.Run();

public class SubscriptionRequest
{
    public int ClassId { get; set; }
    public string EmailAddress{get;set;}
}

record CreatorClassDto(int classId, string className, string classImage, string classDescription, VideoDto[] videos, int creatorId)
{
    public int ClassId = classId;
    public string ClassName = className;
    public string ClassImage = classImage;
    public string ClassDescription = classDescription;
    public VideoDto[] Videos = videos;
    public int CreatorId = creatorId;
}

public class Subscription
{
    public string Id { get; set; }
    public string UserId { get; set; }

    public string ClassId { get; set; }

    public string EmailAddress{get;set;}
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

    public ICollection<Video> Videos { get; set; }
}

public class Video
{
    public Video()
    {

    }
    public string VideoId { get; set; }
    public string Name { get; set; }
    public string VideoSrc { get; set; }
    public string Description { get; set; }
    public int CreatorId { get; set; }

    public int Seconds { get; set; }
}

record VideoDto(int videoId, string title, string description, string videoSrc, int seconds)
{
    public int VideoId = videoId;
    public string Title = title;
    public string Description = description;
    public string VideoSrc = videoSrc;
    public int Seconds = seconds;
}

public class CreatorProfile
{
    public string Id { get; set; }
    public string UserId { get; set; }
    public int CreatorId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string YoutubeUrl { get; set; }

    public string ImageSrc { get; set; }
}


public class CreatorProfileDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string YoutubeUrl { get; set; }
}


class ClassesDb : DbContext
{
    public ClassesDb(DbContextOptions options) : base(options) { }
    public DbSet<CreatorClass> Classes { get; set; }
    public DbSet<Subscription> Subscriptions { get; set; }
    public DbSet<CreatorProfile> Creators { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CreatorClass>()
            .HasPartitionKey(b => b.Id);
        modelBuilder.Entity<Subscription>()
       .HasPartitionKey(b => b.Id);
        modelBuilder.Entity<CreatorProfile>().HasPartitionKey(p => p.Id);
    }



}

