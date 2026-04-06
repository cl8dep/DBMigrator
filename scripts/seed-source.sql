-- Seed data for local testing
CREATE TABLE IF NOT EXISTS users (
    id          SERIAL PRIMARY KEY,
    email       VARCHAR(255) NOT NULL,
    first_name  VARCHAR(100) NOT NULL,
    last_name   VARCHAR(100) NOT NULL,
    phone       VARCHAR(50),
    password_hash VARCHAR(255) NOT NULL,
    national_id VARCHAR(50),
    avatar_url  VARCHAR(500)
);

CREATE TABLE IF NOT EXISTS orders (
    id              SERIAL PRIMARY KEY,
    user_id         INTEGER REFERENCES users(id),
    billing_address TEXT,
    notes           TEXT,
    total           NUMERIC(10, 2)
);

INSERT INTO users (email, first_name, last_name, phone, password_hash, national_id, avatar_url) VALUES
    ('john.doe@realcompany.com', 'John', 'Doe', '+1-555-123-4567', '$2b$10$realHash1', '123-45-6789', 'https://cdn.example.com/avatars/1.jpg'),
    ('jane.smith@realcompany.com', 'Jane', 'Smith', '+1-555-987-6543', '$2b$10$realHash2', '987-65-4321', 'https://cdn.example.com/avatars/2.jpg'),
    ('alice.jones@realcompany.com', 'Alice', 'Jones', '+1-555-111-2222', '$2b$10$realHash3', '456-78-9012', NULL);

INSERT INTO orders (user_id, billing_address, notes, total) VALUES
    (1, '123 Main St, Springfield, IL 62701', 'Rush delivery please', 99.99),
    (2, '456 Oak Ave, Portland, OR 97201', NULL, 249.50),
    (3, '789 Pine Rd, Austin, TX 73301', 'Leave at door', 15.00);
