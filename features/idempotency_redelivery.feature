@idempotency @applied
Feature: Redelivery is harmless due to AppliedEvent ledger

  Scenario: Destination receives the same EventId twice
    Given Destination has APPLIED_EVENT with EventId=E
    When  Destination receives EventId=E again
    Then  no changes to BID/AUCTION occur
    And   APPLIED_EVENT remains unchanged
