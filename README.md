# ToolShopCRM

CRM for a tool and construction equipment shop.

## Technology Stack

**Backend:** C#, ASP.NET Core Web API, Entity Framework Core, PostgreSQL
**Frontend:** React, TypeScript, Tailwind CSS

## Project Structure

ToolShopCRM/
├── backend/
│ ├── ToolShop.API/ # Web API, controllers, entry point
│ ├── ToolShop.Domain/ # Business entities, domain logic
│ └── ToolShop.Infrastructure/ # EF Core, database access
└── frontend/
└── toolshop-client/ # React + TypeScript app


## Prerequisites

- .NET SDK 10
- Node.js 20+ and npm
- PostgreSQL 16+ (running locally)

## Running the Backend

cd backend/ToolShop.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=toolshop_crm;Username=postgres;Password=YOUR_PASSWORD"
dotnet run


API available at `http://localhost:5252`
API docs (Scalar) at `http://localhost:5252/scalar/v1`

## Running the Frontend

cd frontend/toolshop-client
npm install
npm run dev


App available at `http://localhost:5173`

## Database

Requires a local PostgreSQL instance with a database named `toolshop_crm`.
Connection string is managed via .NET User Secrets (not committed to source control).