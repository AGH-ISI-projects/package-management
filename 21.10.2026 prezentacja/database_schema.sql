create extension if not exists postgis;

create table users (
    id serial primary key,
    public_id UUID default gen_random_uuid() unique,
    email varchar(100) not null unique,
    password_hash varchar(255) not null,
    token varchar(128) unique,
    role varchar(20) not null check (role in ('CLIENT', 'VENDOR', 'COURIER')),
    first_name varchar(50) not null,
    last_name varchar(50) not null,
    phone varchar(20) not null,
    created_at timestamp not null default current_timestamp
);

create table vendor_profiles (
    user_id integer primary key references users(id) on delete cascade,
    company_name varchar(100) not null,
    location geography(Point, 4326) not null,
    avg_rating decimal(3,2) not null default 0.00
);

create table courier_profiles (
    user_id integer primary key references users(id) on delete cascade,
    is_available boolean not null default false,
    current_location geography(Point, 4326),
    last_ping_at timestamp,
    avg_rating decimal(3,2) not null default 0.00
);

create table products (
    id serial primary key,
    vendor_id integer not null,
    name varchar(100) not null,
    price decimal(10,2) not null check (price >= 0),
    is_active boolean not null default true,
    constraint fk_product_vendor
        foreign key (vendor_id) references vendor_profiles(user_id) on delete cascade
);

create table orders (
    id serial primary key,
    client_id integer not null,
    vendor_id integer not null,
    courier_id integer,
    status varchar(30) not null default 'ORDERED' CHECK (
        status in ('ORDERED', 'ACCEPTED', 'PREPARATION', 'SHIPPING', 'DELIVERED', 'REJECTED', 'CANCELLED')
    ),
    delivery_location geography(Point, 4326) not null,
    delivery_address varchar(100) not null,
    total_price decimal(10, 2) not null check (total_price >= 0),
    created_at timestamp not null default current_timestamp,
    constraint fk_order_client
        foreign key (client_id) references users(id) on delete restrict,
    constraint fk_order_vendor
        foreign key (vendor_id) references vendor_profiles(user_id) on delete restrict,
    constraint fk_order_courier
        foreign key (courier_id) references courier_profiles(user_id) on delete set null
);

create table order_items (
    id serial primary key,
    order_id integer not null,
    product_id integer not null,
    quantity integer not null check (quantity > 0),
    unit_price decimal(10,2) not null check (unit_price >= 0),
    constraint fk_item_order
        foreign key (order_id) references orders(id) on delete cascade,
    constraint fk_item_product
        foreign key (product_id) references products(id) on delete restrict
);

create table payments (
    id serial primary key,
    order_id integer not null unique,
    amount decimal(10,2) not null check (amount >= 0),
    status varchar(20) not null default 'PENDING' check (
        status in ('PENDING', 'COMPLETED', 'FAILED')
    ),
    external_tx_id varchar(100),
    updated_at timestamp not null default current_timestamp,
    constraint fk_payment_order
        foreign key (order_id) references orders(id) on delete cascade
);

create table ratings (
    id serial primary key,
    order_id integer not null,
    client_id integer not null,
    target_user_id integer not null,
    target_role varchar(20) not null check (target_role in ('VENDOR', 'COURIER')),
    score integer not null check (score between 1 and 5),
    comment varchar(50) not null,
    created_at timestamp not null default current_timestamp,
    constraint fk_rating_order
        foreign key (order_id) references orders(id) on delete cascade,
    constraint fk_rating_client
        foreign key (client_id) references users(id) on delete cascade,
    constraint fk_rating_target
        foreign key (target_user_id) references users(id) on delete cascade,
    constraint uq_order_target_rating unique (order_id, target_role)
);

create table notifications (
    id serial primary key,
    user_id integer not null,
    order_id integer not null,
    message varchar(255) not null,
    is_read boolean not null default false,
    created_at timestamp not null default current_timestamp,
    constraint fk_notification_user
        foreign key (user_id) references users(id) on delete cascade,
    constraint fk_notification_order
        foreign key (order_id) references orders(id) on delete cascade
);

create index idx_courier_location on courier_profiles using gist (current_location);
