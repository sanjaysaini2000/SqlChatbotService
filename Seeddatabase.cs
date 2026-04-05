using Microsoft.Data.Sqlite;

namespace SqlChatbot.Data;

/// <summary>
/// Seeds a sample online shopping SQLite database for chatbot testing.
///
/// Schema tables:
///   customers, addresses, categories, products, product_images,
///   carts, cart_items, orders, order_items, payments, reviews, wishlist
///
/// Usage in Program.cs:
///   await SeedDatabase.InitializeAsync(
///       builder.Configuration["Database:ConnectionString"]!);
/// </summary>
public static class SeedDatabase
{
    public static async Task<bool> IsInitializedAsync(string connectionString)
    {
        try
        {
            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM customers";
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task InitializeAsync(string connectionString)
    {
        // Check if already initialized
        if (await IsInitializedAsync(connectionString))
        {
            Console.WriteLine("ℹ️  Database already initialized, skipping.");
            return;
        }

        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await CreateTablesAsync(conn);
        await SeedDataAsync(conn);

        Console.WriteLine("✅ Database seeded successfully.");
    }

    // ── DDL ────────────────────────────────────────────────────────────────
    private static async Task CreateTablesAsync(SqliteConnection conn)
    {
        var ddl = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            -- Customers
            CREATE TABLE IF NOT EXISTS customers (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                name          TEXT    NOT NULL,
                email         TEXT    NOT NULL UNIQUE,
                phone         TEXT,
                gender        TEXT    CHECK(gender IN ('M','F','Other')),
                date_of_birth TEXT,
                joined_on     TEXT    NOT NULL
            );

            -- Shipping / billing addresses
            CREATE TABLE IF NOT EXISTS addresses (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id   INTEGER NOT NULL REFERENCES customers(id),
                label         TEXT    NOT NULL,
                street        TEXT    NOT NULL,
                city          TEXT    NOT NULL,
                state         TEXT,
                postal_code   TEXT    NOT NULL,
                country       TEXT    NOT NULL DEFAULT 'Singapore',
                is_default    INTEGER NOT NULL DEFAULT 0
            );

            -- Product categories (self-referencing for subcategories)
            CREATE TABLE IF NOT EXISTS categories (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                name          TEXT    NOT NULL,
                parent_id     INTEGER REFERENCES categories(id)
            );

            -- Products
            CREATE TABLE IF NOT EXISTS products (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                category_id   INTEGER NOT NULL REFERENCES categories(id),
                name          TEXT    NOT NULL,
                description   TEXT,
                brand         TEXT,
                sku           TEXT    NOT NULL UNIQUE,
                price         REAL    NOT NULL,
                discount_pct  REAL    NOT NULL DEFAULT 0,
                stock_qty     INTEGER NOT NULL DEFAULT 0,
                is_active     INTEGER NOT NULL DEFAULT 1
            );

            -- Product images
            CREATE TABLE IF NOT EXISTS product_images (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id    INTEGER NOT NULL REFERENCES products(id),
                url           TEXT    NOT NULL,
                is_primary    INTEGER NOT NULL DEFAULT 0
            );

            -- Shopping carts (one active cart per customer)
            CREATE TABLE IF NOT EXISTS carts (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id   INTEGER NOT NULL REFERENCES customers(id),
                created_at    TEXT    NOT NULL,
                updated_at    TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cart_items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                cart_id       INTEGER NOT NULL REFERENCES carts(id),
                product_id    INTEGER NOT NULL REFERENCES products(id),
                quantity      INTEGER NOT NULL DEFAULT 1
            );

            -- Orders
            CREATE TABLE IF NOT EXISTS orders (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id     INTEGER NOT NULL REFERENCES customers(id),
                address_id      INTEGER NOT NULL REFERENCES addresses(id),
                ordered_on      TEXT    NOT NULL,
                status          TEXT    NOT NULL CHECK(status IN
                                  ('pending','confirmed','processing',
                                   'shipped','delivered','cancelled','refunded')),
                shipping_fee    REAL    NOT NULL DEFAULT 0,
                coupon_code     TEXT,
                discount_amount REAL    NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS order_items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id      INTEGER NOT NULL REFERENCES orders(id),
                product_id    INTEGER NOT NULL REFERENCES products(id),
                quantity      INTEGER NOT NULL,
                unit_price    REAL    NOT NULL,
                discount_pct  REAL    NOT NULL DEFAULT 0
            );

            -- Payments
            CREATE TABLE IF NOT EXISTS payments (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                order_id      INTEGER NOT NULL REFERENCES orders(id),
                method        TEXT    NOT NULL CHECK(method IN
                                ('credit_card','debit_card','paynow',
                                 'grabpay','bank_transfer','cod')),
                status        TEXT    NOT NULL CHECK(status IN
                                ('pending','completed','failed','refunded')),
                amount        REAL    NOT NULL,
                paid_on       TEXT
            );

            -- Reviews
            CREATE TABLE IF NOT EXISTS reviews (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                product_id    INTEGER NOT NULL REFERENCES products(id),
                customer_id   INTEGER NOT NULL REFERENCES customers(id),
                order_id      INTEGER NOT NULL REFERENCES orders(id),
                rating        INTEGER NOT NULL CHECK(rating BETWEEN 1 AND 5),
                title         TEXT,
                comment       TEXT,
                reviewed_on   TEXT    NOT NULL
            );

            -- Wishlist
            CREATE TABLE IF NOT EXISTS wishlist (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                customer_id   INTEGER NOT NULL REFERENCES customers(id),
                product_id    INTEGER NOT NULL REFERENCES products(id),
                added_on      TEXT    NOT NULL,
                UNIQUE(customer_id, product_id)
            );
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Seed orchestrator ──────────────────────────────────────────────────
    private static async Task SeedDataAsync(SqliteConnection conn)
    {
        await using (var chk = conn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM customers";
            var count = (long)(await chk.ExecuteScalarAsync())!;
            if (count > 0)
            {
                Console.WriteLine("ℹ️  Database already seeded, skipping.");
                return;
            }
        }

        await using var tx = await conn.BeginTransactionAsync();

        await InsertCategoriesAsync(conn);
        await InsertCustomersAsync(conn);
        await InsertAddressesAsync(conn);
        await InsertProductsAsync(conn);
        await InsertProductImagesAsync(conn);
        await InsertOrdersAsync(conn);
        await InsertPaymentsAsync(conn);
        await InsertReviewsAsync(conn);
        await InsertCartsAsync(conn);
        await InsertWishlistAsync(conn);

        await tx.CommitAsync();
    }

    // ── Categories ─────────────────────────────────────────────────────────
    private static async Task InsertCategoriesAsync(SqliteConnection conn)
    {
        var rows = new (int id, string name, int? parent)[]
        {
            (1,  "Electronics",       null),
            (2,  "Phones & Tablets",  1),
            (3,  "Computers",         1),
            (4,  "Audio",             1),
            (5,  "Cameras",           1),
            (6,  "Fashion",           null),
            (7,  "Men's Clothing",    6),
            (8,  "Women's Clothing",  6),
            (9,  "Footwear",          6),
            (10, "Home & Living",     null),
            (11, "Furniture",         10),
            (12, "Kitchen",           10),
            (13, "Sports",            null),
            (14, "Gym & Fitness",     13),
            (15, "Outdoor",           13),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO categories (id, name, parent_id) VALUES ($id, $name, $pid)";
        var pId   = cmd.Parameters.Add("$id",   SqliteType.Integer);
        var pName = cmd.Parameters.Add("$name", SqliteType.Text);
        var pPid  = cmd.Parameters.Add("$pid",  SqliteType.Integer);

        foreach (var (id, name, parent) in rows)
        {
            pId.Value   = id;
            pName.Value = name;
            pPid.Value  = parent.HasValue ? parent.Value : DBNull.Value;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Customers ──────────────────────────────────────────────────────────
    private static async Task InsertCustomersAsync(SqliteConnection conn)
    {
        var rows = new[]
        {
            ("Alice Tan",      "alice.tan@gmail.com",       "+6591234001", "F", "1992-03-14", "2023-01-10"),
            ("Bob Lim",        "bob.lim@gmail.com",         "+6591234002", "M", "1988-07-22", "2023-02-05"),
            ("Carol Ng",       "carol.ng@hotmail.com",      "+6591234003", "F", "1995-11-30", "2023-03-18"),
            ("David Sharma",   "david.sharma@gmail.com",    "+6591234004", "M", "1990-05-09", "2023-04-01"),
            ("Eve Chen",       "eve.chen@yahoo.com",        "+6591234005", "F", "1997-01-25", "2023-05-22"),
            ("Frank Ho",       "frank.ho@gmail.com",        "+6591234006", "M", "1985-09-17", "2023-06-14"),
            ("Grace Park",     "grace.park@gmail.com",      "+6591234007", "F", "1993-12-08", "2023-07-30"),
            ("Henry Wu",       "henry.wu@outlook.com",      "+6591234008", "M", "1991-04-03", "2023-08-19"),
            ("Isla Brown",     "isla.brown@gmail.com",      "+6591234009", "F", "1996-08-20", "2023-09-07"),
            ("James Lee",      "james.lee@gmail.com",       "+6591234010", "M", "1989-02-11", "2023-10-25"),
            ("Karen Yap",      "karen.yap@hotmail.com",     "+6591234011", "F", "1994-06-16", "2023-11-13"),
            ("Leo Santos",     "leo.santos@gmail.com",      "+6591234012", "M", "1987-10-29", "2023-12-01"),
            ("Mia Zhang",      "mia.zhang@gmail.com",       "+6591234013", "F", "1998-04-04", "2024-01-09"),
            ("Nathan Patel",   "nathan.patel@gmail.com",    "+6591234014", "M", "1992-08-18", "2024-02-14"),
            ("Olivia Kim",     "olivia.kim@yahoo.com",      "+6591234015", "F", "1999-12-27", "2024-03-22"),
            ("Peter Goh",      "peter.goh@gmail.com",       "+6591234016", "M", "1986-03-05", "2024-04-10"),
            ("Quinn Raj",      "quinn.raj@gmail.com",       "+6591234017", "M", "1993-07-14", "2024-05-01"),
            ("Rachel Teo",     "rachel.teo@outlook.com",    "+6591234018", "F", "1995-11-09", "2024-06-17"),
            ("Sam Kwok",       "sam.kwok@gmail.com",        "+6591234019", "M", "1990-01-21", "2024-07-03"),
            ("Tina Ong",       "tina.ong@gmail.com",        "+6591234020", "F", "1997-05-30", "2024-08-28"),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO customers (name, email, phone, gender, date_of_birth, joined_on)
            VALUES ($name, $email, $phone, $gender, $dob, $joined)
            """;

        var pName   = cmd.Parameters.Add("$name",   SqliteType.Text);
        var pEmail  = cmd.Parameters.Add("$email",  SqliteType.Text);
        var pPhone  = cmd.Parameters.Add("$phone",  SqliteType.Text);
        var pGender = cmd.Parameters.Add("$gender", SqliteType.Text);
        var pDob    = cmd.Parameters.Add("$dob",    SqliteType.Text);
        var pJoined = cmd.Parameters.Add("$joined", SqliteType.Text);

        foreach (var (name, email, phone, gender, dob, joined) in rows)
        {
            pName.Value = name; pEmail.Value = email; pPhone.Value = phone;
            pGender.Value = gender; pDob.Value = dob; pJoined.Value = joined;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Addresses ──────────────────────────────────────────────────────────
    private static async Task InsertAddressesAsync(SqliteConnection conn)
    {
        var rows = new[]
        {
            (1,  "Home",   "10 Orchard Rd #05-01",     "Singapore", "528841", 1),
            (1,  "Office", "1 Raffles Pl #20-00",       "Singapore", "048616", 0),
            (2,  "Home",   "22 Bukit Timah Rd",         "Singapore", "229733", 1),
            (3,  "Home",   "55 Clementi Ave 3 #12-04",  "Singapore", "129908", 1),
            (4,  "Home",   "88 Tampines St 21 #03-10",  "Singapore", "521088", 1),
            (5,  "Home",   "30 Jurong East St 31",      "Singapore", "609463", 1),
            (6,  "Home",   "14 Bedok North Ave 4",      "Singapore", "469662", 1),
            (7,  "Home",   "3 Yishun Ring Rd #08-02",   "Singapore", "768675", 1),
            (8,  "Home",   "101 Toa Payoh Lor 1",       "Singapore", "319579", 1),
            (8,  "Office", "6 Shenton Way #10-01",      "Singapore", "068809", 0),
            (9,  "Home",   "200 Pasir Ris St 21",       "Singapore", "510200", 1),
            (10, "Home",   "77 Sengkang East Dr",       "Singapore", "544077", 1),
            (11, "Home",   "50 Ang Mo Kio Ave 8",       "Singapore", "569814", 1),
            (12, "Home",   "19 Woodlands Ave 1",        "Singapore", "739051", 1),
            (13, "Home",   "8 Punggol Field Walk",      "Singapore", "828727", 1),
            (14, "Home",   "33 Bishan St 22 #14-100",   "Singapore", "570033", 1),
            (15, "Home",   "9 Geylang Bahru Lane",      "Singapore", "339628", 1),
            (16, "Home",   "21 Sembawang Cres #07-01",  "Singapore", "757677", 1),
            (17, "Home",   "40 Hougang St 51 #02-20",   "Singapore", "538928", 1),
            (18, "Home",   "66 Choa Chu Kang Ave 4",    "Singapore", "680066", 1),
            (19, "Home",   "12 Upper Changi Rd North",  "Singapore", "507989", 1),
            (20, "Home",   "5 Kallang Ave #01-00",      "Singapore", "339382", 1),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO addresses (customer_id, label, street, city, postal_code, country, is_default)
            VALUES ($cid, $label, $street, $city, $postal, 'Singapore', $default)
            """;

        var pCid     = cmd.Parameters.Add("$cid",     SqliteType.Integer);
        var pLabel   = cmd.Parameters.Add("$label",   SqliteType.Text);
        var pStreet  = cmd.Parameters.Add("$street",  SqliteType.Text);
        var pCity    = cmd.Parameters.Add("$city",    SqliteType.Text);
        var pPostal  = cmd.Parameters.Add("$postal",  SqliteType.Text);
        var pDefault = cmd.Parameters.Add("$default", SqliteType.Integer);

        foreach (var (cid, label, street, city, postal, isDefault) in rows)
        {
            pCid.Value = cid; pLabel.Value = label; pStreet.Value = street;
            pCity.Value = city; pPostal.Value = postal; pDefault.Value = isDefault;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Products ───────────────────────────────────────────────────────────
    private static async Task InsertProductsAsync(SqliteConnection conn)
    {
        var rows = new[]
        {
            // (cat_id, name, description, brand, sku, price, discount_pct, stock)
            // Phones & Tablets
            (2, "Galaxy S25 Ultra",        "Latest Samsung flagship with AI camera",    "Samsung",     "SAM-S25U",    1599.00, 5.0,  40),
            (2, "iPhone 16 Pro",           "Apple A18 Pro chip, titanium finish",       "Apple",       "APL-I16P",    1799.00, 0.0,  25),
            (2, "Xiaomi 14T Pro",          "Snapdragon 8 Gen 3, 144Hz display",         "Xiaomi",      "XMI-14TP",     899.00, 10.0, 60),
            (2, "iPad Air M3",             "10.9\" M3 chip tablet",                     "Apple",       "APL-IPAM3",    899.00, 0.0,  30),
            (2, "Samsung Tab S10+",        "12.4\" AMOLED, S-Pen included",             "Samsung",     "SAM-TS10P",    999.00, 8.0,  35),
            // Computers
            (3, "MacBook Air M4",          "15\" lightweight powerhouse",               "Apple",       "APL-MBA-M4",  1799.00, 0.0,  20),
            (3, "Dell XPS 15",             "Intel Core Ultra 9, OLED display",          "Dell",        "DEL-XPS15",   2299.00, 5.0,  15),
            (3, "Logitech MX Keys S",      "Advanced wireless keyboard",                "Logitech",    "LOG-MXKS",     149.00, 0.0, 150),
            (3, "LG 27\" 4K Monitor",      "IPS panel, USB-C 96W charging",             "LG",          "LG-27UK850",   649.00, 12.0, 25),
            (3, "Anker USB-C Hub 10-in-1", "10 ports, 100W PD passthrough",             "Anker",       "ANK-UCH10",     79.00, 0.0, 200),
            // Audio
            (4, "Sony WH-1000XM6",         "Industry-leading ANC headphones",           "Sony",        "SNY-XM6",      449.00, 10.0, 55),
            (4, "AirPods Pro 3",           "H3 chip, adaptive audio",                   "Apple",       "APL-APP3",     329.00, 0.0,  80),
            (4, "JBL Charge 6",            "Portable waterproof Bluetooth speaker",      "JBL",         "JBL-CHG6",     199.00, 5.0, 100),
            (4, "Bose QuietComfort 45",    "Comfortable ANC over-ear headphones",        "Bose",        "BSE-QC45",     379.00, 15.0, 45),
            // Cameras
            (5, "Sony A7 V",               "Full-frame mirrorless, 61MP sensor",         "Sony",        "SNY-A7V",     3499.00, 0.0,  10),
            (5, "GoPro Hero 13 Black",     "Action camera, 5.3K60 video",               "GoPro",       "GPR-H13B",     549.00, 8.0,  40),
            // Men's Clothing
            (7, "Uniqlo Dry-EX T-Shirt",   "Breathable sport tee, size M",              "Uniqlo",      "UNQ-DEXT-M",    29.90, 0.0, 300),
            (7, "Levi's 511 Slim Jeans",   "Classic slim fit, W32 L32",                 "Levi's",      "LVI-511-M",     89.90, 10.0, 120),
            // Women's Clothing
            (8, "Zara Linen Dress",        "Breathable summer dress, size S",            "Zara",        "ZRA-LIND-S",    59.90, 20.0, 80),
            (8, "H&M Floral Blouse",       "Light floral print blouse, size M",          "H&M",         "HNM-FLOB-M",    35.90, 0.0, 150),
            // Footwear
            (9, "Nike Air Max 270",        "Lifestyle shoe, size UK9",                  "Nike",        "NKE-AM270-9",  179.00, 0.0,  60),
            (9, "Adidas Ultraboost 24",    "Premium running shoe, size UK8",             "Adidas",      "ADI-UB24-8",   219.00, 10.0, 45),
            // Furniture
            (11,"IKEA MARKUS Chair",       "Ergonomic office chair, black",             "IKEA",        "IKA-MARK-BK",  299.00, 0.0,  25),
            (11,"FlexiSpot E7 Desk",       "Electric height-adjustable standing desk",  "FlexiSpot",   "FLX-E7-DESK",  699.00, 5.0,  12),
            // Kitchen
            (12,"Instant Pot Duo 7-in-1",  "7-in-1 pressure cooker, 6Qt",              "Instant Pot", "IPT-DUO6",     129.00, 0.0,  70),
            (12,"Nespresso Vertuo Pop",    "Capsule coffee machine",                    "Nespresso",   "NES-VPOP",     149.00, 10.0, 55),
            // Gym & Fitness
            (14,"Resistance Band Set",     "5 resistance levels, latex-free",           "FitPro",      "FTP-RBAND",     25.90, 0.0, 250),
            (14,"Yoga Mat Premium 6mm",    "Non-slip, eco-friendly TPE material",       "Liforme",     "LIF-YM6MM",     89.00, 0.0,  90),
            // Outdoor
            (15,"Hydro Flask 32oz",        "Insulated stainless steel water bottle",    "Hydro Flask", "HYF-32OZ",      55.00, 0.0, 130),
            (15,"Decathlon Trek 100 Bag",  "20L hiking backpack, lightweight",          "Decathlon",   "DEC-TK100",     49.90, 0.0,  85),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (category_id, name, description, brand, sku, price, discount_pct, stock_qty)
            VALUES ($cat, $name, $desc, $brand, $sku, $price, $disc, $stock)
            """;

        var pCat   = cmd.Parameters.Add("$cat",   SqliteType.Integer);
        var pName  = cmd.Parameters.Add("$name",  SqliteType.Text);
        var pDesc  = cmd.Parameters.Add("$desc",  SqliteType.Text);
        var pBrand = cmd.Parameters.Add("$brand", SqliteType.Text);
        var pSku   = cmd.Parameters.Add("$sku",   SqliteType.Text);
        var pPrice = cmd.Parameters.Add("$price", SqliteType.Real);
        var pDisc  = cmd.Parameters.Add("$disc",  SqliteType.Real);
        var pStock = cmd.Parameters.Add("$stock", SqliteType.Integer);

        foreach (var (cat, name, desc, brand, sku, price, disc, stock) in rows)
        {
            pCat.Value = cat; pName.Value = name; pDesc.Value = desc;
            pBrand.Value = brand; pSku.Value = sku; pPrice.Value = price;
            pDisc.Value = disc; pStock.Value = stock;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Product Images ─────────────────────────────────────────────────────
    private static async Task InsertProductImagesAsync(SqliteConnection conn)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO product_images (product_id, url, is_primary)
            SELECT id,
                   'https://cdn.shopcdn.com/products/' || sku || '/main.jpg',
                   1
            FROM   products
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Orders + Order Items ───────────────────────────────────────────────
    private static async Task InsertOrdersAsync(SqliteConnection conn)
    {
        var orders = new (int cid, int addrId, string date, string status,
                          double fee, string? coupon, double disc,
                          (int pid, int qty)[] items)[]
        {
            // Jan 2026 — all delivered
            (1,  1,  "2026-01-03", "delivered", 3.99, null,       0,    [(11,1),(13,1)]),
            (2,  3,  "2026-01-05", "delivered", 0,    "SAVE10",  12.0,  [(1,1)]),
            (3,  4,  "2026-01-08", "delivered", 3.99, null,       0,    [(19,1),(27,1)]),
            (4,  5,  "2026-01-10", "delivered", 0,    null,       0,    [(6,1)]),
            (5,  6,  "2026-01-12", "delivered", 3.99, null,       0,    [(8,1),(10,2)]),
            (6,  7,  "2026-01-15", "delivered", 0,    "FREESHIP", 3.99, [(2,1)]),
            (7,  8,  "2026-01-18", "delivered", 3.99, null,       0,    [(17,1),(18,1)]),
            (8,  9,  "2026-01-20", "delivered", 0,    null,       0,    [(25,1)]),
            (9,  11, "2026-01-22", "delivered", 3.99, null,       0,    [(29,2),(30,1)]),
            (10, 12, "2026-01-25", "delivered", 0,    null,       0,    [(14,1)]),

            // Feb 2026 — all delivered
            (11, 13, "2026-02-02", "delivered", 3.99, null,       0,    [(21,1),(22,1)]),
            (12, 14, "2026-02-05", "delivered", 0,    "SAVE10",  17.9,  [(26,1)]),
            (13, 15, "2026-02-08", "delivered", 3.99, null,       0,    [(3,1)]),
            (14, 16, "2026-02-10", "delivered", 0,    null,       0,    [(7,1)]),
            (15, 17, "2026-02-13", "delivered", 3.99, null,       0,    [(4,1),(28,1)]),
            (1,  1,  "2026-02-15", "delivered", 0,    null,       0,    [(12,1)]),
            (2,  3,  "2026-02-18", "delivered", 3.99, null,       0,    [(23,1)]),
            (3,  4,  "2026-02-20", "delivered", 0,    "SAVE20",  11.9,  [(20,1)]),
            (4,  5,  "2026-02-22", "delivered", 3.99, null,       0,    [(9,1)]),
            (5,  6,  "2026-02-25", "delivered", 0,    null,       0,    [(16,1)]),

            // Mar 2026 — mixed statuses
            (6,  7,  "2026-03-01", "delivered",   3.99, null,       0,    [(5,1)]),
            (7,  8,  "2026-03-04", "delivered",   0,    null,       0,    [(11,1),(13,1)]),
            (8,  9,  "2026-03-06", "delivered",   3.99, "SAVE10",  14.9, [(1,1)]),
            (9,  11, "2026-03-08", "delivered",   0,    null,       0,    [(24,1)]),
            (10, 12, "2026-03-11", "shipped",     3.99, null,       0,    [(6,1),(8,1)]),
            (11, 13, "2026-03-13", "shipped",     0,    null,       0,    [(2,1)]),
            (12, 14, "2026-03-15", "shipped",     3.99, null,       0,    [(27,1),(29,1)]),
            (13, 15, "2026-03-17", "processing",  3.99, null,       0,    [(15,1)]),
            (14, 16, "2026-03-19", "processing",  0,    "FREESHIP", 3.99, [(10,2),(30,1)]),
            (15, 17, "2026-03-22", "cancelled",   0,    null,       0,    [(7,1)]),

            // Apr 2026 — recent / pending
            (16, 18, "2026-04-01", "confirmed", 3.99, null,       0,    [(3,1),(28,1)]),
            (17, 19, "2026-04-02", "pending",   0,    null,       0,    [(12,1)]),
            (18, 20, "2026-04-03", "pending",   3.99, "SAVE10",  21.9, [(1,1)]),
            (19, 21, "2026-04-04", "pending",   0,    null,       0,    [(26,1),(29,2)]),
            (20, 22, "2026-04-05", "pending",   3.99, null,       0,    [(4,1),(8,1)]),
        };

        await using var orderCmd = conn.CreateCommand();
        orderCmd.CommandText = """
            INSERT INTO orders (customer_id, address_id, ordered_on, status,
                                shipping_fee, coupon_code, discount_amount)
            VALUES ($cid, $addr, $date, $status, $fee, $coupon, $disc);
            SELECT last_insert_rowid();
            """;
        var pCid    = orderCmd.Parameters.Add("$cid",    SqliteType.Integer);
        var pAddr   = orderCmd.Parameters.Add("$addr",   SqliteType.Integer);
        var pDate   = orderCmd.Parameters.Add("$date",   SqliteType.Text);
        var pStatus = orderCmd.Parameters.Add("$status", SqliteType.Text);
        var pFee    = orderCmd.Parameters.Add("$fee",    SqliteType.Real);
        var pCoupon = orderCmd.Parameters.Add("$coupon", SqliteType.Text);
        var pDisc   = orderCmd.Parameters.Add("$disc",   SqliteType.Real);

        await using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = """
            INSERT INTO order_items (order_id, product_id, quantity, unit_price, discount_pct)
            SELECT $oid, $pid, $qty, price, discount_pct
            FROM   products WHERE id = $pid
            """;
        var pOid = itemCmd.Parameters.Add("$oid", SqliteType.Integer);
        var pPid = itemCmd.Parameters.Add("$pid", SqliteType.Integer);
        var pQty = itemCmd.Parameters.Add("$qty", SqliteType.Integer);

        foreach (var (cid, addrId, date, status, fee, coupon, disc, items) in orders)
        {
            pCid.Value    = cid;
            pAddr.Value   = addrId;
            pDate.Value   = date;
            pStatus.Value = status;
            pFee.Value    = fee;
            pCoupon.Value = coupon is null ? DBNull.Value : coupon;
            pDisc.Value   = disc;

            var orderId = (long)(await orderCmd.ExecuteScalarAsync())!;

            foreach (var (pid, qty) in items)
            {
                pOid.Value = orderId; pPid.Value = pid; pQty.Value = qty;
                await itemCmd.ExecuteNonQueryAsync();
            }
        }
    }

    // ── Payments ───────────────────────────────────────────────────────────
    private static async Task InsertPaymentsAsync(SqliteConnection conn)
    {
        var methods = new[] { "credit_card", "paynow", "grabpay", "debit_card", "bank_transfer" };

        await using var fetchCmd = conn.CreateCommand();
        fetchCmd.CommandText = """
            SELECT o.id,
                   o.status,
                   o.shipping_fee,
                   o.discount_amount,
                   COALESCE(SUM(oi.unit_price * oi.quantity * (1 - oi.discount_pct / 100.0)), 0)
            FROM   orders o
            LEFT   JOIN order_items oi ON oi.order_id = o.id
            GROUP  BY o.id
            ORDER  BY o.id
            """;

        var orderData = new List<(long id, string status, double fee, double disc, double items)>();
        await using (var r = await fetchCmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync())
                orderData.Add((r.GetInt64(0), r.GetString(1),
                               r.GetDouble(2), r.GetDouble(3), r.GetDouble(4)));
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO payments (order_id, method, status, amount, paid_on)
            VALUES ($oid, $method, $pstatus, $amount, $paidon)
            """;
        var pOid     = cmd.Parameters.Add("$oid",     SqliteType.Integer);
        var pMethod  = cmd.Parameters.Add("$method",  SqliteType.Text);
        var pPStatus = cmd.Parameters.Add("$pstatus", SqliteType.Text);
        var pAmount  = cmd.Parameters.Add("$amount",  SqliteType.Real);
        var pPaidOn  = cmd.Parameters.Add("$paidon",  SqliteType.Text);

        int i = 0;
        foreach (var (id, status, fee, disc, itemsTotal) in orderData)
        {
            var total  = Math.Round(itemsTotal + fee - disc, 2);
            var method = methods[i % methods.Length]; i++;

            var (pStatus, paidOn) = status switch
            {
                "delivered" or "shipped" or "processing" or "confirmed"
                    => ("completed", $"2026-0{(i % 3) + 1}-{(10 + i % 18):D2}"),
                "cancelled"
                    => ("refunded",  (string?)null),
                _   => ("pending",   (string?)null),
            };

            pOid.Value     = id;
            pMethod.Value  = method;
            pPStatus.Value = pStatus;
            pAmount.Value  = total;
            pPaidOn.Value  = paidOn is null ? DBNull.Value : paidOn;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Reviews ────────────────────────────────────────────────────────────
    private static async Task InsertReviewsAsync(SqliteConnection conn)
    {
        var rows = new[]
        {
            // (product_id, customer_id, order_id, rating, title, comment, reviewed_on)
            (11, 1,  1,  5, "Great headphones!", "Amazing ANC, very comfortable for long use.",         "2026-01-18"),
            (13, 6,  2,  4, "Good speaker",      "Loud and clear, battery life is solid.",              "2026-01-22"),
            (19, 3,  3,  5, "Love the dress",    "Perfect fit and very breathable fabric.",             "2026-01-20"),
            (6,  4,  4,  4, "Solid laptop",      "Fast performance, build quality is excellent.",       "2026-01-25"),
            (8,  5,  5,  3, "Decent keyboard",   "Good tactile feedback but a bit loud.",               "2026-01-28"),
            (2,  6,  6,  5, "Flagship quality",  "Best iPhone I've owned, camera is stunning.",         "2026-02-01"),
            (17, 7,  7,  4, "Nice t-shirt",      "Soft material and dries quickly after a run.",        "2026-02-05"),
            (25, 8,  8,  5, "Great chair",       "Very ergonomic, back pain reduced noticeably.",       "2026-02-08"),
            (29, 9,  9,  5, "Love the bands",    "Great set, all resistance levels are very useful.",   "2026-02-10"),
            (14, 10, 10, 4, "Good headphones",   "Comfortable, ANC is nearly as good as Sony.",         "2026-02-15"),
            (21, 11, 11, 3, "Decent shoes",      "Fits true to size but sole wears out a bit fast.",   "2026-02-20"),
            (26, 12, 12, 5, "Coffee game changed","Quick, easy, and the coffee tastes great.",           "2026-02-22"),
            (3,  13, 13, 4, "Good Xiaomi phone", "Great value, camera is impressive for the price.",    "2026-02-25"),
            (7,  14, 14, 5, "Best laptop ever",  "Dell XPS 15 OLED screen is absolutely gorgeous.",    "2026-03-01"),
            (4,  15, 15, 4, "Nice iPad",         "Fast and light. Great for reading and work.",         "2026-03-05"),
            (12, 1,  16, 5, "AirPods are magic", "Seamless switching between all my Apple devices.",   "2026-03-08"),
            (23, 2,  17, 4, "Comfy chair",       "Takes time to assemble but absolutely worth it.",    "2026-03-12"),
            (20, 3,  18, 5, "Stylish blouse",    "Exactly as pictured, shipping was really fast.",     "2026-03-16"),
            (9,  4,  19, 3, "Monitor is okay",   "Colors are good but the stand feels a bit flimsy.", "2026-03-20"),
            (16, 5,  20, 5, "GoPro is amazing",  "5.3K footage is incredibly smooth and sharp.",       "2026-03-24"),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO reviews (product_id, customer_id, order_id, rating, title, comment, reviewed_on)
            VALUES ($pid, $cid, $oid, $rating, $title, $comment, $date)
            """;

        var pPid     = cmd.Parameters.Add("$pid",     SqliteType.Integer);
        var pCid     = cmd.Parameters.Add("$cid",     SqliteType.Integer);
        var pOid     = cmd.Parameters.Add("$oid",     SqliteType.Integer);
        var pRating  = cmd.Parameters.Add("$rating",  SqliteType.Integer);
        var pTitle   = cmd.Parameters.Add("$title",   SqliteType.Text);
        var pComment = cmd.Parameters.Add("$comment", SqliteType.Text);
        var pDate    = cmd.Parameters.Add("$date",    SqliteType.Text);

        foreach (var (pid, cid, oid, rating, title, comment, date) in rows)
        {
            pPid.Value = pid; pCid.Value = cid; pOid.Value = oid;
            pRating.Value = rating; pTitle.Value = title;
            pComment.Value = comment; pDate.Value = date;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ── Carts ──────────────────────────────────────────────────────────────
    private static async Task InsertCartsAsync(SqliteConnection conn)
    {
        var carts = new (int cid, string created, string updated, (int pid, int qty)[] items)[]
        {
            (16, "2026-04-03", "2026-04-05", [(1,1),(10,1)]),
            (17, "2026-04-04", "2026-04-05", [(11,1)]),
            (18, "2026-04-04", "2026-04-04", [(6,1),(28,1)]),
            (19, "2026-04-05", "2026-04-05", [(21,2)]),
            (20, "2026-04-05", "2026-04-05", [(29,1),(30,1)]),
        };

        await using var cartCmd = conn.CreateCommand();
        cartCmd.CommandText = """
            INSERT INTO carts (customer_id, created_at, updated_at)
            VALUES ($cid, $created, $updated);
            SELECT last_insert_rowid();
            """;
        var pCid     = cartCmd.Parameters.Add("$cid",     SqliteType.Integer);
        var pCreated = cartCmd.Parameters.Add("$created", SqliteType.Text);
        var pUpdated = cartCmd.Parameters.Add("$updated", SqliteType.Text);

        await using var itemCmd = conn.CreateCommand();
        itemCmd.CommandText = """
            INSERT INTO cart_items (cart_id, product_id, quantity)
            VALUES ($cartid, $pid, $qty)
            """;
        var pCartId = itemCmd.Parameters.Add("$cartid", SqliteType.Integer);
        var pPid    = itemCmd.Parameters.Add("$pid",    SqliteType.Integer);
        var pQty    = itemCmd.Parameters.Add("$qty",    SqliteType.Integer);

        foreach (var (cid, created, updated, items) in carts)
        {
            pCid.Value = cid; pCreated.Value = created; pUpdated.Value = updated;
            var cartId = (long)(await cartCmd.ExecuteScalarAsync())!;

            foreach (var (pid, qty) in items)
            {
                pCartId.Value = cartId; pPid.Value = pid; pQty.Value = qty;
                await itemCmd.ExecuteNonQueryAsync();
            }
        }
    }

    // ── Wishlist ───────────────────────────────────────────────────────────
    private static async Task InsertWishlistAsync(SqliteConnection conn)
    {
        var rows = new[]
        {
            (1,  5,  "2026-02-10"), (1,  15, "2026-03-01"), (2,  1,  "2026-01-20"),
            (2,  24, "2026-02-15"), (3,  6,  "2026-01-25"), (3,  12, "2026-03-10"),
            (4,  11, "2026-02-05"), (5,  7,  "2026-01-30"), (5,  22, "2026-03-05"),
            (6,  3,  "2026-02-20"), (7,  14, "2026-01-15"), (8,  9,  "2026-03-15"),
            (9,  2,  "2026-02-25"), (10, 25, "2026-01-10"), (11, 16, "2026-03-20"),
            (12, 4,  "2026-02-08"), (13, 8,  "2026-03-02"), (14, 13, "2026-01-18"),
            (15, 29, "2026-03-25"), (16, 17, "2026-02-14"),
        };

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO wishlist (customer_id, product_id, added_on)
            VALUES ($cid, $pid, $date)
            """;
        var pCid  = cmd.Parameters.Add("$cid",  SqliteType.Integer);
        var pPid  = cmd.Parameters.Add("$pid",  SqliteType.Integer);
        var pDate = cmd.Parameters.Add("$date", SqliteType.Text);

        foreach (var (cid, pid, date) in rows)
        {
            pCid.Value = cid; pPid.Value = pid; pDate.Value = date;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}