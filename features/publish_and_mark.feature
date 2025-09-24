@outbox @publish @us
Feature: EventPublisher marks outbox rows as published

  Scenario: Mark Published=true after successful publish
    Given US EVENT_OUTBOX has a "BidPlaced" row Published=false
    When  US EventPublisher publishes pending
    Then  that row is updated to Published=true with PublishedAtUtc set
