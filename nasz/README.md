# Package Delivery & Courier Management System

Academic project designed and developed for the **Software Engineering** course at **AGH University of Krakow** (Faculty of Electrical Engineering, Automatics, Computer Science and Biomedical Engineering).

## 📌 Project Overview

A modular logistics and delivery platform supporting the end-to-end lifecycle of parcel and food deliveries. The system coordinates three distinct actor roles:

- **Client (`CLIENT`)**: Browses nearby vendors, manages carts, places and pays for orders, tracks delivery status in real-time, and rates vendors/couriers.
- **Vendor (`VENDOR`)**: Manages product/menu catalogs, accepts or rejects incoming orders, and updates preparation status.
- **Courier (`COURIER`)**: Toggles availability, transmits live GPS telemetry, accepts assigned deliveries, picks up parcels, and marks orders as delivered.

## ⚙️ Architecture & Tech Stack

- **Frontend**: Dedicated panels for Clients, Vendors, and Couriers; communicates via HTTP REST API and WebSockets for real-time order notifications.
- **Backend**: Modular business logic including an Order State Machine, Geocoding & Courier Dispatching, Product Catalog, Payments, and Notifications.
- **Primary Database**: **PostgreSQL** with **PostGIS** (`geography(Point, 4326)` & GiST spatial indexes) for persistent transactional data and spatial storage.
- **In-Memory & Geolocation Layer**: **Redis** utilizing `GEOADD`/`GEOSEARCH`, TTL heartbeats, and atomic soft-locking to handle high-frequency courier GPS pings and prevent race conditions during dispatch.

### Order Lifecycle

```
ORDERED ➔ ACCEPTED ➔ PREPARATION ➔ SHIPPING ➔ DELIVERED
   │          │
   ├──────────┴─► REJECTED (Vendor)
   └────────────► CANCELLED (Client)
```

## 📁 Repository Structure

- `21.10.2026 prezentacja/` — Stage 2 materials: system architecture diagrams, UML use cases, database schema (`database_schema.sql`), LaTeX report (`main.tex`), and presentation slides.
- `28.10.2026 final/` — Final stage deliverables and Redis data management diagrams.

## 👥 Authors

Project created by:
- Filip Kontek
- Marcel Kosmala
- Igor Krzystek

**Supervisor**: dr inż. Marek Zachara  
**Field of Study**: Computer Science and Intelligent Systems (Informatyka i Systemy Inteligentne), AGH UST
