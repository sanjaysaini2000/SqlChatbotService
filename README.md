# SQL Chatbot Service

A .NET 8 REST API service that converts natural language questions into SQL queries using Google's Gemini AI model and executes them against a comprehensive SQLite e-commerce database called **Shoppy**.

## Overview

This project demonstrates the integration of:
- **Google AI (Gemini 1.5 Flash)** - For natural language processing and SQL generation
- **Microsoft Semantic Kernel** - For AI orchestration
- **SQLite** - For data storage and querying
- **ASP.NET Core Web API** - For RESTful endpoints with Swagger/OpenAPI documentation
- **Sample E-Commerce Database** - 12 tables with 200+ records of realistic shopping data

## How It Works

The chatbot service processes user queries through the following pipeline:

1. **Schema Introspection** - Reads the SQLite database schema (tables and columns)
2. **Prompt Engineering** - Sends schema and natural language query to Gemini 1.5 Flash
3. **SQL Generation** - AI generates a valid SQLite SELECT query
4. **Query Execution** - Executes the generated SQL against the Shoppy database
5. **Result Summarization** - Uses Gemini AI to summarize results in natural language

## Prerequisites

- **.NET 8 SDK or later** - [Download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **Google AI API Key** - Get one from [Google AI Studio](https://aistudio.google.com/app/apikey)
- **SQLite** - Included with the project dependencies

## Database Schema (Shoppy)

The project comes with a pre-seeded e-commerce database with 12 tables:

| Table | Records | Purpose |
|-------|---------|---------|
| **customers** | 20 | Singapore-based customers |
| **addresses** | 22 | Shipping and billing addresses |
| **categories** | 15 | Product categories (hierarchical) |
| **products** | 30 | Electronics, fashion, sports items |
| **product_images** | 30 | Product image URLs |
| **carts** | 5 | Active shopping carts |
| **cart_items** | 12 | Items in carts |
| **orders** | 35+ | Orders from Jan-Apr 2026 |
| **order_items** | 60+ | Line items in orders |
| **payments** | 35+ | Payment records (credit card, PayNow, GrabPay, etc.) |
| **reviews** | 20 | Product reviews with ratings |
| **wishlist** | 20 | Customer wishlists |

**Sample Data Highlights:**
- 20 customers from Singapore with realistic profiles
- 30 products (phones, laptops, audio, cameras, clothing, furniture, kitchen tools, fitness, outdoor)
- 35+ orders spanning Jan-Apr 2026
- Multiple order statuses: pending, confirmed, processing, shipped, delivered, cancelled, refunded
- 5 payment methods: credit_card, debit_card, PayNow, GrabPay, bank_transfer
- 20 product reviews with ratings (1-5 stars)
- Real-world pricing and discounts

## Setup

### 1. Clone or Extract the Project

```bash
cd SqlChatbotService
```

### 2. Configure API Keys

Edit the `.env` file and add your Google AI API key:

```
GOOGLE_API_KEY=your_actual_api_key_from_google_ai_studio
GOOGLE_MODEL_ID=gemini-1.5-flash
```

### 3. Database Initialization

The Shoppy database (`shoppy.db`) is **automatically created and seeded on first run**:

```bash
dotnet run
```

**Output on first run:**
```
✅ Database seeded successfully.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5270
```

**Output on subsequent runs:**
```
ℹ️  Database already initialized, skipping.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5270
```

The database initialization is **idempotent** — it checks if data exists before seeding, so you can safely restart the application.

### 4. Database Connection

The connection string is configured in `appsettings.json`:

```json
"Database": {
  "ConnectionString": "Data Source=shoppy.db"
}
```

## Running the Application

### Development

```bash
# Restore packages
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run
```

The API will start on **`http://localhost:5270`** (Development) or **`https://localhost:7051`** (HTTPS).

### Production Build

```bash
dotnet build --configuration Release
dotnet run --configuration Release
```

## API Documentation

### Swagger / OpenAPI UI

Interactive API documentation is available at:

```
http://localhost:5270/swagger
```

You can test all endpoints directly from the Swagger interface.

### OpenAPI Specification

The OpenAPI JSON spec is available at:
```
http://localhost:5270/swagger/v1/swagger.json
```

## API Usage

### POST /api/chat

Submit a natural language question. The AI will generate a SQL query, execute it, and summarize the results.

**Request:**
```json
{
  "prompt": "How many products are in stock?"
}
```

**Response:**
```json
{
  "answer": "Based on the database, there are 30 products currently in stock..."
}
```

### Example Queries

Try these natural language prompts:

**E-Commerce Queries:**
- "Show me all orders from 2026"
- "What's the most expensive product?"
- "How many customers are from Singapore?"
- "List products with a discount"
- "Show me orders that are still pending"
- "What are the top 5 products by price?"
- "How many customers have made purchases?"
- "Show me the average product price by category"
- "What payment methods were used in April?"
- "List products with reviews rating 5 stars"

**Administrative Queries:**
- "Count customers by gender"
- "Which orders had the highest shipping fee?"
- "Show me cancelled orders"
- "List all product categories"
- "How much revenue from delivered orders?"

## Project Structure

```
SqlChatbotService/
├── Controllers/
│   └── ChatController.cs           # API /chat endpoint
├── Services/
│   └── SqlChatService.cs           # AI + SQL + execution logic
├── Properties/
│   └── launchSettings.json         # Launch configuration
├── appsettings.json                # App configuration
├── appsettings.Development.json    # Dev settings
├── Program.cs                      # Application startup
├── Seeddatabase.cs                 # Database schema & seed data
├── .env                            # Google AI credentials (git-ignored)
├── .gitignore                      # Git exclusions
├── README.md                       # This file
└── SqlChatbotService.csproj        # Project file
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| Microsoft.AspNetCore.OpenApi | 8.0.0 | OpenAPI support |
| Microsoft.Data.Sqlite | 8.0.0 | SQLite data access |
| Microsoft.SemanticKernel | 1.74.0 | AI orchestration |
| Microsoft.SemanticKernel.Connectors.Google | 1.74.0-alpha | Gemini integration |
| Swashbuckle.AspNetCore | 6.4.0 | Swagger/OpenAPI UI |

## Features

✅ Natural language to SQL conversion using Gemini AI  
✅ Automatic database schema detection and introspection  
✅ Query result summarization in natural language  
✅ RESTful API with comprehensive Swagger documentation  
✅ Pre-populated e-commerce database with 200+ records  
✅ Error handling and validation  
✅ Environment-based configuration  
✅ Idempotent database initialization  
✅ SELECT-only queries (read-safe, no writes)

## Settings & Configuration

### AI Model

The default Gemini model is `gemini-1.5-flash`. Other options:
- `gemini-1.5-pro` (more capable, slower)
- `gemini-2.0-flash` (newest, if available)

Change in `.env`:
```
GOOGLE_MODEL_ID=gemini-1.5-pro
```

### Logging

Configured in `appsettings.json`:
```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Microsoft.AspNetCore": "Warning"
  }
}
```

## Limitations & Notes

- **SELECT queries only** - Only SELECT statements are permitted for safety
- **Result limit** - Results are capped at 100 rows to prevent large payloads
- **AI-generated SQL** - Quality depends on prompt clarity and schema complexity
- **API key quota** - Monitor your Google AI Studio API usage and quota limits
- **Database constraints** - Foreign keys are enforced (PRAGMA foreign_keys = ON)

## Troubleshooting

### "GOOGLE_API_KEY not found"
- Ensure `.env` file exists in the project root
- Verify the API key format (no extra spaces or quotes)
- Restart the application after editing `.env`

### "Database connection failed"
- Check that `shoppy.db` is in the project directory
- Ensure the application has write permissions
- Verify the connection string in `appsettings.json`

### "404 Not Found on Swagger"
- Verify the application is running and listening on `http://localhost:5270`
- Try accessing `http://localhost:5270/swagger`
- Check browser console for network errors

### Build or Runtime Errors
```bash
# Clean and rebuild
rm -r bin obj
dotnet restore --no-cache
dotnet build
dotnet run
```

## API Response Examples

### Successful Query
```json
{
  "answer": "There are 30 products in the system. Products span across 8 categories including Electronics (10 items), Fashion (4 items), Furniture (2 items), and others. Stock levels vary from 0 to 300 units."
}
```

### Query with No Results
```json
{
  "answer": "No results found."
}
```

### Error Response
```json
{
  "answer": "Error: Only SELECT queries are permitted."
}
```

## Future Enhancements

- [ ] Support for UPDATE/DELETE queries with role-based authorization
- [ ] Query caching and performance optimization
- [ ] Advanced retry logic for AI API failures
- [ ] Multi-language support
- [ ] Query history and analytics
- [ ] Custom database import functionality

## License

This project is provided as-is for educational and development purposes.

## Support & References

- **Google AI**: [Google AI Documentation](https://ai.google.dev/docs)
- **Semantic Kernel**: [Microsoft SK Docs](https://learn.microsoft.com/en-us/semantic-kernel/overview/)
- **SQLite**: [SQLite Documentation](https://www.sqlite.org/docs.html)
- **ASP.NET Core**: [Microsoft Docs](https://learn.microsoft.com/en-us/aspnet/core/)
