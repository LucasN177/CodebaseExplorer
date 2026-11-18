import React from 'react';
import { OrderService } from '@acme/core';
import { Chart } from 'chart.js';
import { Missing } from './missing';

export function App(): React.ReactElement {
  const service = new OrderService();
  return <div title={String(Chart) + String(Missing)}>{String(service)}</div>;
}
