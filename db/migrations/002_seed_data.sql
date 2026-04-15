-- FanPad Service Health Monitor - Seed Data
-- Establishes initial service configurations and routing defaults

-- ─────────────────────────────────────────────
-- SERVICE CONFIGURATIONS
-- ─────────────────────────────────────────────

INSERT INTO service_configs (id, provider, service_type, display_name, is_primary, priority, is_enabled, config_json, status_page_url) VALUES
-- Email: Mailgun (primary)
('a1000000-0000-0000-0000-000000000001', 'mailgun', 'email', 'Mailgun', TRUE,  1, TRUE,
 '{"api_base": "https://api.mailgun.net/v3", "domain": "sandbox.mailgun.org", "test_recipient": "health@fanpad.test"}',
 'https://status.mailgun.com'),

-- Email: AWS SES (fallback)
('a1000000-0000-0000-0000-000000000002', 'ses',     'email', 'AWS SES', FALSE, 2, TRUE,
 '{"region": "us-east-1", "test_recipient": "health@fanpad.test", "configuration_set": "fanpad-health"}',
 'https://health.aws.amazon.com'),

-- SMS: Twilio (primary)
('a1000000-0000-0000-0000-000000000003', 'twilio',  'sms',   'Twilio',  TRUE,  1, TRUE,
 '{"api_base": "https://api.twilio.com/2010-04-01", "test_to": "+15005550006", "test_from": "+15005550001"}',
 'https://status.twilio.com');

-- ─────────────────────────────────────────────
-- INITIAL ROUTING STATES  (all set to primary)
-- ─────────────────────────────────────────────

INSERT INTO routing_states (service_type, active_service_config_id, action, reason, changed_by) VALUES
('email', 'a1000000-0000-0000-0000-000000000001', 'auto', 'Initial configuration - Mailgun as primary email provider', 'system'),
('sms',   'a1000000-0000-0000-0000-000000000003', 'auto', 'Initial configuration - Twilio as primary SMS provider',   'system');

-- ─────────────────────────────────────────────
-- SAMPLE CAMPAIGNS  (for demo)
-- ─────────────────────────────────────────────

INSERT INTO campaigns (id, name, artist_name, service_types, scheduled_at, gate_status) VALUES
('c0000000-0000-0000-0000-000000000001', 'Summer Tour Announcement', 'The Midnight', ARRAY['email','sms']::service_type[], NOW() + INTERVAL '2 hours', 'go'),
('c0000000-0000-0000-0000-000000000002', 'Album Pre-Save Drop',       'Novo Amor',    ARRAY['email']::service_type[],       NOW() + INTERVAL '6 hours', 'go'),
('c0000000-0000-0000-0000-000000000003', 'VIP Ticket Flash Sale',     'Phoebe Bridgers', ARRAY['sms']::service_type[],      NOW() + INTERVAL '1 hour',  'go'),
('c0000000-0000-0000-0000-000000000004', 'Merch Drop Alert',          'Hozier',       ARRAY['email','sms']::service_type[], NOW() + INTERVAL '4 hours', 'go');
