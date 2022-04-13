using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAdB2C"));
builder.Services.AddCors(options => options.AddPolicy("allowAny", o => o.AllowAnyOrigin()));
builder.Services.AddAuthorization();

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



app.MapGet("/classes", () =>
{
    var classes = new List<CreatorClass>() {new CreatorClass(1234, "How to train your yorkie",
    "https://www.petplate.com/wp-content/uploads/2021/03/AdobeStock_236757188.jpeg",
    "A great class about great dogs!", new List<Video>{new Video(555, "Intro", "Getting Started with your furry pal", "https://www.youtube.com/watch?v=_9CUjcf0NSI", 605),
    new Video(654, "Potty Traing, PU", "Get your dog going where he needs to!", "https://www.youtube.com/watch?v=V4iOkvBWTys", 12001)}.ToArray(), 1),
    new CreatorClass(2345, "Nailing Jello to a Wall", "https://linkedstrategies.com/wp-content/uploads/2020/05/Why-cant-I-make-this-work-scaled.jpg", "How to do the impossible!",
    new List<Video>{new Video(620, "Intro", "Can it be done?", "https://www.youtube.com/watch?v=8ePy_mnH774", 605)}.ToArray(), 1)};
    return classes;
})
.WithName("GetClasses");

app.MapGet("user/classes", (HttpContext context) =>
{
    context.VerifyUserHasAnyAcceptedScope(new string[] { "access_as_user" });

    var classes = new List<CreatorClass>() {new CreatorClass(1234, "How to train your yorkie",
    "https://www.petplate.com/wp-content/uploads/2021/03/AdobeStock_236757188.jpeg",
    "A great class about great dogs!", new List<Video>{new Video(555, "Intro", "Getting Started with your furry pal", "https://www.youtube.com/watch?v=_9CUjcf0NSI", 605),
    new Video(654, "Potty Traing, PU", "Get your dog going where he needs to!", "https://www.youtube.com/watch?v=V4iOkvBWTys", 12001)}.ToArray(), 1),
    new CreatorClass(2345, "Nailing Jello to a Wall", "https://linkedstrategies.com/wp-content/uploads/2020/05/Why-cant-I-make-this-work-scaled.jpg", "How to do the impossible!",
    new List<Video>{new Video(620, "Intro", "Can it be done?", "https://www.youtube.com/watch?v=8ePy_mnH774", 605)}.ToArray(), 1)};
    return classes;
})
.WithName("GetClassesForUser").RequireAuthorization();

app.Run();

record CreatorClass(int classId, string className, string classImage, string classDescription, Video[] videos, int creatorId)
{
    public int ClassId = classId;
    public string ClassName = className;
    public string ClassImage = classImage;
    public string ClassDescription = classDescription;
    public Video[] Videos = videos;
    public int CratorId = creatorId;
}

record Video(int videoId, string title, string description, string videoSrc, int seconds)
{
    public int VideoId = videoId;
    public string Title = title;
    public string Description = description;
    public string VideoSrc = videoSrc;
    public int Seconds = seconds;
}

